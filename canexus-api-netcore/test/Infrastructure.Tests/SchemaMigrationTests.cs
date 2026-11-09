
using System.Text.RegularExpressions;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// 迁移执行器集成测试：在独立临时库上验证按序执行、幂等重跑与单迁移事务回滚。
/// <para>框架不记录迁移历史，因此每次 <c>ApplyAsync</c> 都会执行清单中的全部脚本；
/// 临时库（super_admin_migration_test_&lt;guid&gt;）在测试结束后 DROP，不污染共享测试库。</para>
/// </summary>
[TestFixture]
public class SchemaMigrationTests
{
    private string _tempDatabaseName = null!;
    private string _adminConnection = null!;
    private SqlSugarScope _client = null!;
    private SchemaMigrator _migrator = null!;

    /// <summary>一次性初始化：建独立临时库并装配迁移执行器。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;
        _tempDatabaseName = $"super_admin_migration_test_{Guid.NewGuid():N}";
        var tempConnection = Regex.Replace(
            connectionString, "(?i)database=[^;]+", $"database={_tempDatabaseName}");
        _adminConnection = Regex.Replace(
            connectionString, "(?i)database=[^;]+", "database=super_admin_test");

        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(_adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync($"CREATE DATABASE IF NOT EXISTS `{_tempDatabaseName}`");
        }

        _client = new SqlSugarScope(CreateConnectionConfig(tempConnection));
        _migrator = new SchemaMigrator(_client, NullLogger<SchemaMigrator>.Instance);
    }

    /// <summary>一次性清理：释放客户端并删除临时库。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await _client.Ado.ExecuteCommandAsync($"DROP DATABASE IF EXISTS `{_tempDatabaseName}`");
        }
        catch
        {
            // 初始化失败时临时库可能不存在，清理失败不影响测试结果
        }

        _client.Dispose();
    }

    /// <summary>迁移按 vNNN 顺序执行；脚本幂等时重复执行不报错、不产生半套结构。</summary>
    [Test]
    public async Task Apply_ExecutesInOrder_AndIsIdempotent()
    {
        ISchemaMigration[] migrations =
        [
            new ProbeMigration("v001", "建探针表", scripts:
            [
                "CREATE TABLE IF NOT EXISTS sys_migration_probe (Id CHAR(36) PRIMARY KEY, Label VARCHAR(50)) ENGINE=InnoDB"
            ]),
            new ProbeMigration("v002", "写入幂等探针行", scripts:
            [
                "INSERT INTO sys_migration_probe (Id, Label) VALUES ('v002', 'ok') ON DUPLICATE KEY UPDATE Label = VALUES(Label)"
            ])
        ];

        await _migrator.ApplyAsync(SchemaMigrationCatalog.Order(migrations));
        // 无历史记录：再次启动会重跑全部脚本，幂等脚本必须能安全重复执行
        await _migrator.ApplyAsync(SchemaMigrationCatalog.Order(migrations));

        var tableExists = await TableExistsAsync("sys_migration_probe");
        var rowCount = await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM sys_migration_probe");
        Assert.Multiple(() =>
        {
            Assert.That(tableExists, Is.True, "v001 应先于 v002 执行并建表");
            Assert.That(Convert.ToInt64(rowCount), Is.EqualTo(1), "幂等重跑不应产生重复行");
        });
    }

    /// <summary>单个迁移失败时该迁移自身事务回滚，已成功的迁移写入保留。</summary>
    [Test]
    public async Task Apply_Failure_RollsBackItsOwnTransaction()
    {
        // 探针表由夹具预建（显式 InnoDB：本服务器默认引擎 MyISAM 无事务语义）
        await _client.Ado.ExecuteCommandAsync(
            "CREATE TABLE IF NOT EXISTS sys_migration_ok (Id CHAR(36) PRIMARY KEY, Label VARCHAR(50)) ENGINE=InnoDB");

        ISchemaMigration[] migrations =
        [
            new ProbeMigration("v001", "写入探针行（应成功）", apply: (_, sql, _) =>
                sql.ExecuteAsync("INSERT INTO sys_migration_ok (Id, Label) VALUES ('ok', 'v001') ON DUPLICATE KEY UPDATE Label = VALUES(Label)")),
            new ProbeMigration("v002", "写入后抛错（应回滚）", apply: async (_, sql, _) =>
            {
                await sql.ExecuteAsync("INSERT INTO sys_migration_ok (Id, Label) VALUES ('bad', 'v002')");
                await sql.ExecuteAsync("SELECT * FROM __no_such_table__");
            })
        ];

        Assert.ThrowsAsync<MySqlConnector.MySqlException>(
            () => _migrator.ApplyAsync(SchemaMigrationCatalog.Order(migrations)));

        var labels = await _client.Ado.GetStringAsync("SELECT GROUP_CONCAT(Label ORDER BY Label) FROM sys_migration_ok");
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("v001"), "成功迁移的写入应保留");
            Assert.That(labels, Does.Not.Contain("v002"), "失败迁移应整体回滚，其写入不应残留");
        });
    }

    /// <summary>空迁移清单为无操作（开发期默认状态：结构完全由 CodeFirst 依实体模型生成）。</summary>
    [Test]
    public async Task Apply_EmptyList_IsNoOp()
    {
        await _migrator.ApplyAsync([]);
        Assert.Pass("空清单不抛异常");
    }

    /// <summary>构造与生产一致（SqlSugarClientFactory 口径）的连接配置。</summary>
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

    /// <summary>以 information_schema 判定表是否存在。</summary>
    /// <param name="tableName">表名。</param>
    /// <returns>存在返回 true。</returns>
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var count = await _client.Ado.GetScalarAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table",
            new { db = _tempDatabaseName, table = tableName });
        return Convert.ToInt64(count) > 0;
    }

    /// <summary>测试用迁移：可声明幂等脚本，也可注入需要事务执行器的自定义逻辑。</summary>
    private sealed class ProbeMigration(
        string migrationId,
        string description,
        IReadOnlyList<string>? scripts = null,
        Func<ISqlSugarClient, ITransactionSqlRunner, CancellationToken, Task>? apply = null) : ISchemaMigration
    {
        /// <inheritdoc />
        public string MigrationId { get; } = migrationId;

        /// <inheritdoc />
        public string Description { get; } = description;

        /// <inheritdoc />
        public IReadOnlyList<string> Scripts { get; } = scripts ?? [];

        /// <inheritdoc />
        public async Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
        {
            if (apply is not null)
            {
                await apply(client, sql, cancellationToken);
            }
        }
    }
}
