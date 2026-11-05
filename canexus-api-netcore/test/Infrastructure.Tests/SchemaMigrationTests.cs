
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// 数据库迁移机制集成测试（P1-A3）：连接真实 MySQL，在独立临时库上验证
/// 基线登记、增量迁移的顺序应用 / 幂等 / 事务回滚、重复迁移冲突与「只改实体不补迁移」启动校验。
/// <para>临时库（super_admin_migration_test_&lt;guid&gt;）在测试结束后 DROP，不污染共享测试库。</para>
/// </summary>
[TestFixture]
public class SchemaMigrationTests
{
    private string _tempDatabaseName = null!;
    private SqlSugarScope _client = null!;
    private SchemaMigrator _migrator = null!;
    private IReadOnlyList<string> _modelTableNames = [];

    /// <summary>一次性初始化：建独立临时库、按生产口径建模型表，并装配迁移器。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;
        _tempDatabaseName = $"super_admin_migration_test_{Guid.NewGuid():N}";
        var tempConnection = System.Text.RegularExpressions.Regex.Replace(
            connectionString, "(?i)database=[^;]+", $"database={_tempDatabaseName}");

        // 临时库尚不存在：先用已知库（super_admin_test）建库，再切到临时库
        var adminConnection = System.Text.RegularExpressions.Regex.Replace(
            connectionString, "(?i)database=[^;]+", "database=super_admin_test");
        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync("CREATE DATABASE IF NOT EXISTS `" + _tempDatabaseName + "`");
        }

        _client = new SqlSugarScope(CreateConnectionConfig(tempConnection));
        _migrator = new SchemaMigrator(_client, NullLogger<SchemaMigrator>.Instance);

        // 与生产 DbInitializer 一致：扫描 Domain 程序集全部具体实体并 CodeFirst 建表
        var entityTypes = typeof(AggregateBase).Assembly.GetTypes()
            .Where(IsConcreteEntityType)
            .Distinct()
            .ToArray();
        _client.CodeFirst.InitTables(entityTypes);
        _modelTableNames = entityTypes
            .Select(type => _client.EntityMaintenance.GetEntityInfo(type).DbTableName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>一次性清理：释放客户端并删除临时库。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await _client.Ado.ExecuteCommandAsync("DROP DATABASE IF EXISTS `" + _tempDatabaseName + "`");
        }
        catch
        {
            // 初始化失败时临时库可能不存在，清理失败不影响测试结果
        }

        _client.Dispose();
    }

    /// <summary>验证基线登记：记录模型全部表名清单（可审计），重复执行幂等。</summary>
    [Test]
    public async Task Baseline_RecordsModelTables_AndIsIdempotent()
    {
        await _migrator.EnsureBaselineAsync(_modelTableNames);

        var rows = await _client.Queryable<SchemaMigrationRecord>().ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(TableExistsAsync("sys_schema_migration").GetAwaiter().GetResult(), Is.True, "迁移记录表应存在");
            Assert.That(rows, Has.Count.EqualTo(1), "空库首次应仅登记一条基线");
            Assert.That(rows[0].MigrationId, Is.EqualTo(SchemaMigrator.BaselineMigrationId));
            Assert.That(rows[0].AppliedAt, Is.Not.EqualTo(default(DateTime)), "基线应有应用时间（审计）");
            Assert.That(rows[0].TableNames, Does.Contain("sys_user"), "基线表名清单应包含模型表 sys_user");
        });

        // 重复执行：幂等，不重复登记
        await _migrator.EnsureBaselineAsync(_modelTableNames);
        var rowsAfter = await _client.Queryable<SchemaMigrationRecord>().ToListAsync();
        Assert.That(rowsAfter, Has.Count.EqualTo(1), "重复执行基线登记应幂等");
    }

    /// <summary>验证增量迁移按 ID 顺序应用、记录审计行，重复执行不重复应用。</summary>
    [Test]
    public async Task Incremental_AppliesInOrder_AndIsIdempotent()
    {
        await _migrator.EnsureBaselineAsync(_modelTableNames);
        ISchemaMigration[] migrations =
        [
            // 显式 ENGINE=InnoDB：本服务器默认引擎为 MyISAM（DML 自动提交），须对齐生产表引擎才能验证事务语义
            new ProbeMigration("v001", "建探针表", (_, sql, _) => sql.ExecuteAsync("CREATE TABLE sys_migration_probe (Id CHAR(36) PRIMARY KEY, Label VARCHAR(50)) ENGINE=InnoDB")),
            new ProbeMigration("v002", "探针表加列", (_, sql, _) => sql.ExecuteAsync("ALTER TABLE sys_migration_probe ADD COLUMN Extra VARCHAR(50) NULL"))
        ];

        await _migrator.ApplyPendingAsync(migrations);

        var rows = await _client.Queryable<SchemaMigrationRecord>().OrderBy(x => x.MigrationId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(3), "应为基线 + 两条增量");
            Assert.That(rows[1].MigrationId, Is.EqualTo("v001"));
            Assert.That(rows[2].MigrationId, Is.EqualTo("v002"));
            Assert.That(rows[1].AppliedAt, Is.Not.EqualTo(default(DateTime)), "增量应有应用时间（审计）");
            Assert.That(TableExistsAsync("sys_migration_probe").GetAwaiter().GetResult(), Is.True, "迁移 v001 应建表");
        });
        var hasColumn = await _client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'sys_migration_probe' AND COLUMN_NAME = 'Extra'",
            new { db = _tempDatabaseName });
        Assert.That(Convert.ToInt32(hasColumn), Is.EqualTo(1), "迁移 v002 应加列");

        // 重复执行：全部已应用，不重复
        await _migrator.ApplyPendingAsync(migrations);
        var rowsAfter = await _client.Queryable<SchemaMigrationRecord>().ToListAsync();
        Assert.That(rowsAfter, Has.Count.EqualTo(3), "重复执行不应再次应用");
    }

    /// <summary>验证增量迁移失败时该迁移自身事务回滚、不残留记录（生产可回滚）。</summary>
    [Test]
    public async Task Incremental_Failure_RollsBackItsOwnTransaction()
    {
        await _migrator.EnsureBaselineAsync(_modelTableNames);
        // 探针表由夹具预建（显式 InnoDB：本服务器默认引擎 MyISAM 无事务语义），聚焦 DML 回滚断言
        await _client.Ado.ExecuteCommandAsync(
            "CREATE TABLE sys_migration_ok (Id CHAR(36) PRIMARY KEY, Label VARCHAR(50)) ENGINE=InnoDB");

        ISchemaMigration[] migrations =
        [
            new ProbeMigration("r001", "写入探针行（应成功）", (_, sql, _) => sql.ExecuteAsync("INSERT INTO sys_migration_ok (Id, Label) VALUES ('ok', 'r001')")),
            new ProbeMigration("r002", "写入后抛错（应回滚）", async (_, sql, _) =>
            {
                await sql.ExecuteAsync("INSERT INTO sys_migration_ok (Id, Label) VALUES ('bad', 'r002')");
                await sql.ExecuteAsync("SELECT * FROM __no_such_table__");
            })
        ];

        // 迁移器原样透出失败迁移抛出的异常（MySqlException），不包装、不吞掉
        Assert.ThrowsAsync<MySqlConnector.MySqlException>(() => _migrator.ApplyPendingAsync(migrations));

        var rows = await _client.Queryable<SchemaMigrationRecord>().ToListAsync();
        var labels = await _client.Ado.GetStringAsync("SELECT GROUP_CONCAT(Label ORDER BY Label) FROM sys_migration_ok");
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(x => x.MigrationId), Does.Contain("r001"), "成功迁移应留下审计记录");
            Assert.That(rows.Select(x => x.MigrationId), Does.Not.Contain("r002"), "失败迁移不应留下审计记录");
            Assert.That(labels, Does.Contain("r001"), "r001 已提交，其写入应保留");
            Assert.That(labels, Does.Not.Contain("r002"), "r002 失败应整体回滚，其写入不应残留");
        });
    }

    /// <summary>验证模型含未登记迁移的表时启动校验失败（CI 阻止只改实体不补迁移）。</summary>
    [Test]
    public async Task Validation_ModelTableWithoutMigration_Throws()
    {
        await _migrator.EnsureBaselineAsync(_modelTableNames);

        // 对照组：模型表全部已登记 → 通过
        await _migrator.ValidateModelAgainstMigrationsAsync(_modelTableNames, []);

        // 模型新增一张未登记迁移的表 → 校验失败并指明缺失表
        var extra = _modelTableNames.Concat(["sys_should_have_migration"]).ToArray();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _migrator.ValidateModelAgainstMigrationsAsync(extra, []));
        Assert.That(ex!.Message, Does.Contain("sys_should_have_migration"), "校验失败应指明缺失迁移的表");
    }

    /// <summary>验证重复迁移 ID 被拒绝（迁移清单冲突即终止）。</summary>
    [Test]
    public async Task DuplicateMigrationId_ThrowsConflict()
    {
        await _migrator.EnsureBaselineAsync(_modelTableNames);
        ISchemaMigration[] migrations =
        [
            new ProbeMigration("dup-1", "重复 1", (_, _, _) => Task.CompletedTask),
            new ProbeMigration("dup-1", "重复 2", (_, _, _) => Task.CompletedTask)
        ];

        Assert.ThrowsAsync<InvalidOperationException>(() => _migrator.ApplyPendingAsync(migrations));
        var rows = await _client.Queryable<SchemaMigrationRecord>().ToListAsync();
        Assert.That(rows.Select(x => x.MigrationId), Does.Not.Contain("dup-1"), "冲突迁移不应落记录");
    }

    /// <summary>构造与生产一致（SqlSugarClientFactory 口径）的连接配置：字符串 / 可空值类型默认生成可空列。</summary>
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

    /// <summary>以 information_schema 判定表是否存在（DbMaintenance.IsAnyTable 对新建表不可靠，弃用）。</summary>
    /// <param name="tableName">表名。</param>
    /// <returns>存在返回 true。</returns>
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var count = await _client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table",
            new { db = _tempDatabaseName, table = tableName });
        return Convert.ToInt32(count) > 0;
    }

    /// <summary>判断类型是否为具体实体类型（与生产 DbInitializer 口径一致）。</summary>
    private static bool IsConcreteEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
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

    /// <summary>测试用增量迁移：直接执行 SQL 委托（经事务执行器运行，显式挂接迁移事务）。</summary>
    /// <param name="migrationId">迁移 ID。</param>
    /// <param name="description">迁移说明。</param>
    /// <param name="apply">应用逻辑（SqlSugar 客户端 + 事务内 SQL 执行器 + 取消令牌）。</param>
    private sealed class ProbeMigration(
        string migrationId,
        string description,
        Func<ISqlSugarClient, ITransactionSqlRunner, CancellationToken, Task> apply) : ISchemaMigration
    {
        /// <inheritdoc />
        public string MigrationId { get; } = migrationId;

        /// <inheritdoc />
        public string Description { get; } = description;

        /// <inheritdoc />
        public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
        {
            return apply(client, sql, cancellationToken);
        }
    }
}
