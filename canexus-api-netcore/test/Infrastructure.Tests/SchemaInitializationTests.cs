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
/// T-13 三层判定集成测试：在独立临时库验证「结构就绪跳过 DDL / 落后按脚本补齐 /
/// 代码更旧拒绝启动 / 脚本正文变更被发现 / 生产缺表拒绝自动改表」。
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
    private string[] _modelTableNames = [];

    /// <summary>建独立临时库、按生产口径建全量模型表并登记基线。</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        _database = $"super_admin_schema_test_{Guid.NewGuid():N}";
        _connection = Regex.Replace(source, "(?i)database=[^;]+", $"database={_database}");
        _adminConnection = Regex.Replace(source, "(?i)database=[^;]+", "database=super_admin_test");

        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync($"CREATE DATABASE IF NOT EXISTS `{_database}`");
        }

        _client = new SqlSugarScope(CreateConnectionConfig(_connection));
        var entityTypes = typeof(AggregateBase).Assembly.GetTypes()
            .Where(IsConcreteEntityType)
            .Distinct()
            .ToArray();
        _client.CodeFirst.InitTables(entityTypes);
        _modelTableNames =
        [
            .. entityTypes
                .Select(type => _client.EntityMaintenance.GetEntityInfo(type).DbTableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
        ];

        _migrator = new SchemaMigrator(_client, NullLogger<SchemaMigrator>.Instance);
        await _migrator.EnsureBaselineAsync(_modelTableNames);
    }

    /// <summary>删除临时库。</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        using var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection));
        await adminClient.Ado.ExecuteCommandAsync($"DROP DATABASE IF EXISTS `{_database}`");
    }

    /// <summary>结构就绪（版本一致 + 表齐全）时启动不执行任何 DDL：手工新增的列不会被 InitTables 删掉。</summary>
    [Test]
    public async Task Ready_SkipsAllDdl_AndPreservesExtraColumn()
    {
        await _migrator.ApplyPendingAsync(StubMigrations());
        await _client.Ado.ExecuteCommandAsync(
            "ALTER TABLE sys_user ADD COLUMN ProbeExtra VARCHAR(20) NULL");

        var state = new DatabaseInitializationState();
        await CreateInitializer(state, "Testing").InitializeAsync();

        var hasColumn = await ColumnExistsAsync("sys_user", "ProbeExtra");
        var status = await _migrator.GetStatusAsync(_modelTableNames, SchemaMigrationCatalog.Order(StubMigrations()));
        Assert.Multiple(() =>
        {
            Assert.That(hasColumn, Is.True, "结构就绪时不得执行 InitTables（V-05 实测 InitTables 会删列）");
            Assert.That(state.Snapshot().Database, Is.EqualTo("Ready"));
            Assert.That(state.Snapshot().Schema, Is.EqualTo("Ready"));
            Assert.That(status.Readiness, Is.EqualTo(SchemaReadiness.Ready));
        });
    }

    /// <summary>库中版本高于代码期望时拒绝启动（部署了更旧的代码）。</summary>
    [Test]
    public async Task CodeBehind_RejectsStartup()
    {
        await _migrator.ApplyPendingAsync(StubMigrations());
        await _client.Insertable(SchemaMigrationRecord.CreateIncremental(
            "v999", "未来版本", SchemaMigrationCatalog.ComputeHash("future"), [])).ExecuteCommandAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateInitializer(new DatabaseInitializationState(), "Testing").InitializeAsync());
        Assert.That(exception!.Message, Does.Contain("代码更旧").Or.Contain("高于代码期望版本"));
    }

    /// <summary>版本落后但表齐全：按序执行迁移脚本并推进到期望版本。</summary>
    [Test]
    public async Task Pending_AppliesScripts_AndAdvancesVersion()
    {
        ISchemaMigration[] migrations =
        [
            new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_init_probe (Id INT PRIMARY KEY)"]),
            new StubMigration("v002", [])
        ];

        var state = new DatabaseInitializationState();
        await CreateInitializer(state, "Testing", migrations).InitializeAsync();

        var records = await _client.Queryable<SchemaMigrationRecord>()
            .Select(record => record.MigrationId).ToListAsync();
        var status = await _migrator.GetStatusAsync(
            _modelTableNames, SchemaMigrationCatalog.Order(migrations));
        var probeTableExists = await TableExistsAsync("sys_init_probe");
        Assert.Multiple(() =>
        {
            Assert.That(probeTableExists, Is.True, "v001 脚本正文应被真正执行");
            Assert.That(records, Does.Contain("v001"));
            Assert.That(records, Does.Contain("v002"));
            Assert.That(status.Readiness, Is.EqualTo(SchemaReadiness.Ready));
            Assert.That(state.Snapshot().AppliedVersion, Is.EqualTo(2));
            Assert.That(state.Snapshot().ExpectedVersion, Is.EqualTo(2));
        });
    }

    /// <summary>已应用迁移的脚本正文被改写时，启动校验拒绝继续（禁止改写已应用脚本）。</summary>
    [Test]
    public async Task ScriptBodyChange_IsDetected()
    {
        ISchemaMigration[] applied =
        [
            new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_init_probe (Id INT PRIMARY KEY)"])
        ];
        await _migrator.ApplyPendingAsync(applied);

        // 代码侧脚本正文已变（库中仍记录旧正文指纹）
        ISchemaMigration[] changed =
        [
            new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_init_probe (Id BIGINT PRIMARY KEY)"]),
            new StubMigration("v002", [])
        ];

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateInitializer(new DatabaseInitializationState(), "Testing", changed).InitializeAsync());
        Assert.That(exception!.Message, Does.Contain("脚本正文已变更"));
    }

    /// <summary>Production 且结构未初始化时明确拒绝，且不会自动建表。</summary>
    [Test]
    public async Task Production_Uninitialized_RejectsWithoutDdl()
    {
        await _migrator.ApplyPendingAsync(StubMigrations());
        await _client.Ado.ExecuteCommandAsync("DROP TABLE sys_post");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateInitializer(new DatabaseInitializationState(), "Production").InitializeAsync());
        var postTableExists = await TableExistsAsync("sys_post");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Production 禁止由应用自动改表"));
            Assert.That(postTableExists, Is.False, "拒绝启动不应附带任何建表 DDL");
        });
    }

    /// <summary>迁移脚本由正文计算指纹：已应用记录的 ScriptHash 等于正文哈希（而非 ID + 说明）。</summary>
    [Test]
    public async Task AppliedScriptHash_ReflectsScriptBody()
    {
        ISchemaMigration[] migrations =
        [
            new StubMigration("v001", ["CREATE TABLE IF NOT EXISTS sys_init_probe (Id INT PRIMARY KEY)"]),
            new StubMigration("v002", [])
        ];
        await _migrator.ApplyPendingAsync(migrations);

        var record = await _client.Queryable<SchemaMigrationRecord>()
            .FirstAsync(item => item.MigrationId == "v001");
        Assert.That(record.ScriptHash,
            Is.EqualTo(SchemaMigrationCatalog.ScriptHash(migrations[0])));
    }

    /// <summary>多实例并发启动（分布式锁不可用、靠唯一索引兜底）全部成功且不重复应用迁移。</summary>
    [Test]
    public async Task ConcurrentStartup_AllSucceed_WithoutDuplicateRecords()
    {
        ISchemaMigration[] migrations =
        [
            new StubMigration("v001", ["SELECT 1"]),
            new StubMigration("v002", [])
        ];

        var first = CreateInitializer(new DatabaseInitializationState(), "Testing", migrations);
        var second = CreateInitializer(new DatabaseInitializationState(), "Testing", migrations);
        await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());

        var records = await _client.Queryable<SchemaMigrationRecord>()
            .Where(item => item.MigrationId == "v001").ToListAsync();
        Assert.That(records, Has.Count.EqualTo(1), "并发启动不得重复写入同一迁移记录");
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
            StubMigrations(), new DatabaseInitializationState(), new NoLock(), new StubEnvironment("Testing"));

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
            using var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection));
            await adminClient.Ado.ExecuteCommandAsync($"DROP DATABASE IF EXISTS `{missingName}`");
        }
    }

    /// <summary>装配与生产同构的初始化器（分布式锁桩降级为无锁）。</summary>
    private DbInitializer CreateInitializer(
        DatabaseInitializationState state,
        string environmentName,
        IEnumerable<ISchemaMigration>? migrations = null)
    {
        var options = new DatabaseOption { ConnectionString = _connection };
        return new DbInitializer(
            _client, options, NullLogger<DbInitializer>.Instance, _migrator,
            migrations ?? StubMigrations(), state, new NoLock(), new StubEnvironment(environmentName));
    }

    /// <summary>默认迁移桩（v001 / v002，无脚本）。</summary>
    private static ISchemaMigration[] StubMigrations() =>
    [
        new StubMigration("v001", []),
        new StubMigration("v002", [])
    ];

    /// <summary>以 information_schema 判定表存在。</summary>
    private async Task<bool> TableExistsAsync(string table)
    {
        var count = await _client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table",
            new { db = _database, table });
        return Convert.ToInt64(count) > 0;
    }

    /// <summary>以 information_schema 判定列存在。</summary>
    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        var count = await _client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table AND COLUMN_NAME = @column",
            new { db = _database, table, column });
        return Convert.ToInt64(count) > 0;
    }

    /// <summary>与生产 SqlSugarClientFactory 一致的连接配置。</summary>
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

    /// <summary>测试用迁移桩。</summary>
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

    /// <summary>分布式锁桩：模拟锁不可用，验证唯一索引兜底仍保证幂等。</summary>
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
