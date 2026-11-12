
using System.Text.RegularExpressions;
using Domain.Entities;
using Infrastructure.DataAccess;
using Infrastructure.Shared.DataAccess;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// 三层初始化集成测试：在独立临时库验证「表齐全时不建表 / 缺表时 CodeFirst 建表并执行幂等迁移 /
/// 迁移开关关闭时跳过迁移 / 生产缺表拒绝自动改表 / 库不存在时的建库判定」。
/// <para>每个用例独立建库并在结束时 DROP，不污染共享测试库。</para>
/// </summary>
[TestFixture]
public class SchemaInitializationTests
{
    private string _database = null!;
    private string _connection = null!;
    private string _adminConnection = null!;
    private SqlSugarScope _client = null!;
    private SchemaMigrator _migrator = null!;

    /// <summary>建独立临时库并按生产口径 CodeFirst 建全量模型表。</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        _database = $"super_admin_schema_test_{Guid.NewGuid():N}";
        _connection = Regex.Replace(source, "(?i)database=[^;]+", $"database={_database}");
        _adminConnection = Regex.Replace(source, "(?i)database=[^;]+", "database=super_admin_test");

        await CreateDatabaseAsync(_database);
        _client = new SqlSugarScope(CreateConnectionConfig(_connection));
        var entityTypes = typeof(AggregateBase).Assembly.GetTypes()
            .Where(IsConcreteEntityType)
            .Distinct()
            .ToArray();
        _client.CodeFirst.InitTables(entityTypes);

        _migrator = new SchemaMigrator(_client, NullLogger<SchemaMigrator>.Instance);
    }

    /// <summary>删除临时库。</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await DropDatabaseAsync(_database);
    }

    /// <summary>表齐全时启动不执行建表 DDL：手工新增的列不会被 InitTables 删掉，幂等迁移仍会执行。</summary>
    [Test]
    public async Task Ready_SkipsTableCreation_PreservesExtraColumn_AndRunsMigrations()
    {
        await _client.Ado.ExecuteCommandAsync("ALTER TABLE sys_user ADD COLUMN ProbeExtra VARCHAR(20) NULL");

        var state = new DatabaseInitializationState();
        await CreateInitializer(state, "Testing", [new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_init_probe (Id INT PRIMARY KEY)"])]).InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ColumnExistsAsync("sys_user", "ProbeExtra").GetAwaiter().GetResult(), Is.True,
                "表齐全时不得执行 InitTables（实测 InitTables 会删列）");
            Assert.That(TableExistsAsync("sys_init_probe").GetAwaiter().GetResult(), Is.True, "幂等迁移脚本应被执行");
            Assert.That(state.Snapshot().Database, Is.EqualTo("Ready"));
            Assert.That(state.Snapshot().Schema, Is.EqualTo("Ready"));
        });
    }

    /// <summary>全新空库：缺表时按 CodeFirst 一次性建表并修正引擎，随后执行迁移脚本。</summary>
    [Test]
    public async Task Fresh_CreatesModelTables_AndAppliesMigrations()
    {
        var freshDatabase = $"super_admin_schema_fresh_{Guid.NewGuid():N}";
        var freshConnection = Regex.Replace(
            TestDatabaseSupport.TestConnectionString, "(?i)database=[^;]+", $"database={freshDatabase}");
        await CreateDatabaseAsync(freshDatabase);

        try
        {
            using var freshClient = new SqlSugarScope(CreateConnectionConfig(freshConnection));
            var initializer = new DbInitializer(
                freshClient,
                new DatabaseOption { ConnectionString = freshConnection },
                NullLogger<DbInitializer>.Instance,
                new SchemaMigrator(freshClient, NullLogger<SchemaMigrator>.Instance),
                [new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_fresh_probe (Id INT PRIMARY KEY)"])],
                new DatabaseInitializationState(),
                new NoLock(),
                new StubEnvironment("Testing"));

            await initializer.InitializeAsync();

            var userExists = await ScalarExistsAsync(
                freshClient, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'sys_user'",
                freshDatabase);
            var probeExists = await ScalarExistsAsync(
                freshClient, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'sys_fresh_probe'",
                freshDatabase);
            Assert.Multiple(() =>
            {
                Assert.That(userExists, Is.True, "缺表时应由 CodeFirst 建立模型表");
                Assert.That(probeExists, Is.True, "迁移脚本应在建表后执行");
            });
        }
        finally
        {
            await DropDatabaseAsync(freshDatabase);
        }
    }

    /// <summary>迁移开关关闭时跳过全部迁移脚本，结构由外部自行管理。</summary>
    [Test]
    public async Task MigrationsDisabled_SkipsMigrations()
    {
        var state = new DatabaseInitializationState();
        await CreateInitializer(
            state,
            "Testing",
            [new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_disabled_probe (Id INT PRIMARY KEY)"])],
            migrationsEnabled: false).InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(TableExistsAsync("sys_disabled_probe").GetAwaiter().GetResult(), Is.False,
                "MigrationsEnabled=false 时不应执行任何迁移脚本");
            Assert.That(state.Snapshot().Schema, Is.EqualTo("Ready"));
        });
    }

    /// <summary>Production 且结构未初始化（缺表）时明确拒绝，且不会自动建表。</summary>
    [Test]
    public async Task Production_MissingTables_RejectsWithoutDdl()
    {
        await _client.Ado.ExecuteCommandAsync("DROP TABLE sys_post");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateInitializer(new DatabaseInitializationState(), "Production").InitializeAsync());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Production 禁止由应用自动改表"));
            Assert.That(TableExistsAsync("sys_post").GetAwaiter().GetResult(), Is.False, "拒绝启动不应附带任何建表 DDL");
        });
    }

    /// <summary>库不存在且禁止自动建库时，抛明确的配置异常而非 MySQL 连接异常。</summary>
    [Test]
    public void DatabaseMissing_RejectedClearly()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        var missingName = $"super_admin_missing_{Guid.NewGuid():N}";
        var missingConnection = Regex.Replace(source, "(?i)database=[^;]+", $"database={missingName}");
        var options = new DatabaseOption
        {
            ConnectionString = missingConnection,
            AutoCreateDatabase = false
        };
        using var client = new SqlSugarScope(CreateConnectionConfig(missingConnection));
        var initializer = new DbInitializer(
            client, options, NullLogger<DbInitializer>.Instance,
            new SchemaMigrator(client, NullLogger<SchemaMigrator>.Instance),
            [], new DatabaseInitializationState(), new NoLock(), new StubEnvironment("Testing"));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("禁止应用自动建库"));
            Assert.That(exception.InnerException, Is.Null, "不应伪装成 MySQL 连接异常");
        });
    }

    /// <summary>库不存在且允许自动建库时，经剥离库名的管理连接创建（不报 Unknown database）。</summary>
    [Test]
    public async Task DatabaseMissing_CreatedViaManagementConnection()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        var missingName = $"super_admin_created_{Guid.NewGuid():N}";
        var missingConnection = Regex.Replace(source, "(?i)database=[^;]+", $"database={missingName}");
        var (management, database) = DatabaseSchemaProbe.Split(missingConnection);

        try
        {
            Assert.That(await DatabaseSchemaProbe.DatabaseExistsAsync(management, database), Is.False);
            await DatabaseSchemaProbe.CreateDatabaseAsync(management, database);
            Assert.That(await DatabaseSchemaProbe.DatabaseExistsAsync(management, database), Is.True);
        }
        finally
        {
            await DropDatabaseAsync(missingName);
        }
    }

    /// <summary>表齐全判定：DbMaintenance 元数据下 FindMissingTables 返回空，不触发建表。</summary>
    [Test]
    public void FindMissingTables_AllPresent_ReturnsEmpty()
    {
        var modelTableNames = GetModelTableNames();

        var missing = DatabaseSchemaProbe.FindMissingTables(_client, modelTableNames);

        Assert.That(missing, Is.Empty, "模型表齐全时不得判定为缺表");
    }

    /// <summary>缺表判定：删除一张模型表后 FindMissingTables 应仅返回该表（禁用缓存，读到真实表清单）。</summary>
    [Test]
    public async Task FindMissingTables_OneDropped_ReturnsOnlyThatTable()
    {
        var modelTableNames = GetModelTableNames();
        var dropped = modelTableNames.Single(name => string.Equals(name, "sys_post", StringComparison.OrdinalIgnoreCase));
        await _client.Ado.ExecuteCommandAsync("DROP TABLE sys_post");

        var missing = DatabaseSchemaProbe.FindMissingTables(_client, modelTableNames);

        Assert.Multiple(() =>
        {
            Assert.That(missing.Count, Is.EqualTo(1));
            Assert.That(missing, Does.Contain(dropped));
        });
    }

    /// <summary>两个开关都为 false（ShouldInitializeStructure=false）且缺表时明确拒绝初始化。</summary>
    [Test]
    public async Task BothSwitchesFalse_MissingTables_RejectsInitialization()
    {
        await _client.Ado.ExecuteCommandAsync("DROP TABLE sys_post");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateInitializer(new DatabaseInitializationState(), "Testing",
                autoCreateTable: false, autoSyncStructure: false).InitializeAsync());
        Assert.That(exception!.Message, Does.Contain("无法建立结构"));
    }

    /// <summary>任一开关为 true（ShouldInitializeStructure=true）时缺表允许 CodeFirst 一次性建表。</summary>
    [Test]
    public async Task EitherSwitchTrue_MissingTables_AllowsInitialization()
    {
        await _client.Ado.ExecuteCommandAsync("DROP TABLE sys_post");

        var state = new DatabaseInitializationState();
        await CreateInitializer(state, "Testing",
            autoCreateTable: false, autoSyncStructure: true).InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(state.Snapshot().Schema, Is.EqualTo("Ready"));
            Assert.That(TableExistsAsync("sys_post").GetAwaiter().GetResult(), Is.True,
                "任一开关为 true 时缺表应允许一次性 CodeFirst 建表");
        });
    }

    /// <summary>按生产 DbInitializer 口径取模型表名清单。</summary>
    /// <returns>模型表名清单。</returns>
    private string[] GetModelTableNames()
    {
        return [.. typeof(AggregateBase).Assembly.GetTypes()
            .Where(IsConcreteEntityType)
            .Select(type => _client.EntityMaintenance.GetEntityInfo(type).DbTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>装配与生产同构的初始化器（分布式锁桩降级为无锁）。</summary>
    /// <param name="state">初始化结论状态。</param>
    /// <param name="environmentName">宿主环境名。</param>
    /// <param name="migrations">迁移清单。</param>
    /// <param name="migrationsEnabled">是否启用迁移机制。</param>
    /// <param name="autoCreateTable">AutoCreateTable 开关取值。</param>
    /// <param name="autoSyncStructure">AutoSyncStructure 开关取值。</param>
    /// <returns>初始化器。</returns>
    private DbInitializer CreateInitializer(
        DatabaseInitializationState state,
        string environmentName,
        IEnumerable<ISchemaMigration>? migrations = null,
        bool migrationsEnabled = true,
        bool autoCreateTable = true,
        bool autoSyncStructure = true)
    {
        var options = new DatabaseOption
        {
            ConnectionString = _connection,
            MigrationsEnabled = migrationsEnabled,
            AutoCreateTable = autoCreateTable,
            AutoSyncStructure = autoSyncStructure
        };
        return new DbInitializer(
            _client, options, NullLogger<DbInitializer>.Instance, _migrator,
            migrations ?? [], state, new NoLock(), new StubEnvironment(environmentName));
    }

    /// <summary>以 information_schema 判定表存在。</summary>
    /// <param name="table">表名。</param>
    /// <returns>存在返回 true。</returns>
    private async Task<bool> TableExistsAsync(string table)
    {
        return await ScalarExistsAsync(
            _client,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table",
            _database,
            ("table", table));
    }

    /// <summary>以 information_schema 判定列存在。</summary>
    /// <param name="table">表名。</param>
    /// <param name="column">列名。</param>
    /// <returns>存在返回 true。</returns>
    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        return await ScalarExistsAsync(
            _client,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table AND COLUMN_NAME = @column",
            _database,
            ("table", table),
            ("column", column));
    }

    /// <summary>按 information_schema 计数判定对象存在（参数化，避免拼接表名）。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    /// <param name="sql">计数 SQL。</param>
    /// <param name="database">库名。</param>
    /// <param name="parameters">附加参数。</param>
    /// <returns>计数大于 0 返回 true。</returns>
    private static async Task<bool> ScalarExistsAsync(
        ISqlSugarClient client,
        string sql,
        string database,
        params (string Name, string Value)[] parameters)
    {
        var args = new List<SugarParameter> { new("@db", database) };
        args.AddRange(parameters.Select(item => new SugarParameter("@" + item.Name, item.Value)));
        var count = await client.Ado.GetScalarAsync(sql, args.ToArray());
        return Convert.ToInt64(count) > 0;
    }

    /// <summary>用管理连接创建临时库。</summary>
    /// <param name="name">库名。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task CreateDatabaseAsync(string name)
    {
        using var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection));
        await adminClient.Ado.ExecuteCommandAsync($"CREATE DATABASE IF NOT EXISTS `{name}`");
    }

    /// <summary>用管理连接删除临时库。</summary>
    /// <param name="name">库名。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task DropDatabaseAsync(string name)
    {
        using var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection));
        await adminClient.Ado.ExecuteCommandAsync($"DROP DATABASE IF EXISTS `{name}`");
    }

    /// <summary>与生产 SqlSugarClientFactory 一致的连接配置。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>连接配置。</returns>
    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    {
                        column.IsNullable = true;
                    }
                }
            }
        };
    }

    /// <summary>与生产 DbInitializer 一致的实体扫描口径（含 OutboxMessage）。</summary>
    /// <param name="type">类型。</param>
    /// <returns>是具体实体类型时返回 true。</returns>
    private static bool IsConcreteEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
        }

        if (type == typeof(OutboxMessage))
        {
            return true;
        }

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current == typeof(DomainBase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>测试用迁移桩（幂等脚本）。</summary>
    private sealed class StubMigration(string id, IReadOnlyList<string> scripts) : ISchemaMigration
    {
        /// <inheritdoc />
        public string MigrationId { get; } = id;

        /// <inheritdoc />
        public string Description { get; } = $"桩迁移 {id}";

        /// <inheritdoc />
        public IReadOnlyList<string> Scripts { get; } = scripts;

        /// <inheritdoc />
        public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>分布式锁桩：模拟锁不可用，验证无锁路径仍可完成初始化。</summary>
    private sealed class NoLock : IDistributedLock
    {
        /// <inheritdoc />
        public ValueTask<IAsyncDisposable?> AcquireAsync(
            string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IAsyncDisposable?>(null);
    }

    /// <summary>宿主环境桩。</summary>
    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        /// <inheritdoc />
        public string EnvironmentName { get; set; } = name;

        /// <inheritdoc />
        public string ApplicationName { get; set; } = "Infrastructure.Tests";

        /// <inheritdoc />
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        /// <inheritdoc />
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
