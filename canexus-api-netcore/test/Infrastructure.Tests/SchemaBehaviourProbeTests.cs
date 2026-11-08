using System.Text.RegularExpressions;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// SqlSugar 行为前置验证（V-01 / V-05）：在隔离临时库上实测，结论作为 T-08 写入侧补偿与
/// T-13 三层判定的设计依据。测试结束 DROP 临时库，不污染 super_admin_test。
/// </summary>
[TestFixture]
public class SchemaBehaviourProbeTests
{
    private string _database = null!;
    private string _connection = null!;
    private string _adminConnection = null!;
    private SqlSugarScope _client = null!;

    /// <summary>建独立临时库。</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        _database = $"super_admin_probe_{Guid.NewGuid():N}";
        _connection = Regex.Replace(source, "(?i)database=[^;]+", $"database={_database}");
        _adminConnection = Regex.Replace(source, "(?i)database=[^;]+", "database=super_admin_test");

        await using (var connection = new MySqlConnector.MySqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_database}`";
            await command.ExecuteNonQueryAsync();
        }

        _client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = _connection,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    }

    /// <summary>释放临时库。</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await using var connection = new MySqlConnector.MySqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>经原生驱动查询 information_schema.COLUMNS，避免 ORM 列名转换干扰观察结果。</summary>
    private async Task<Dictionary<string, string>> ReadColumnsAsync(string table)
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new MySqlConnector.MySqlConnection(_connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COLUMN_NAME, COLUMN_TYPE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name";
        command.Parameters.AddWithValue("@schema", _database);
        command.Parameters.AddWithValue("@name", table);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    /// <summary>V-01：QueryFilter 是否作用于 Updateable / Deleteable（决定写入侧是否必须显式补租户条件）。</summary>
    [Test]
    public async Task V01_QueryFilter_DoesNotApplyToUpdateableOrDeleteable()
    {
        _client.CodeFirst.InitTables<ProbeTenantEntity>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await _client.Insertable(new List<ProbeTenantEntity>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantA, Name = "before" },
            new() { Id = Guid.NewGuid(), TenantId = tenantB, Name = "before" }
        }).ExecuteCommandAsync();

        // 与生产同形态：按具体实体注册全局租户过滤器，当前租户固定为 A
        _client.QueryFilter.AddTableFilter<ProbeTenantEntity>(it => it.TenantId == tenantA);

        var queryCount = await _client.Queryable<ProbeTenantEntity>().CountAsync();
        var updateRows = await _client.Updateable<ProbeTenantEntity>()
            .SetColumns(it => it.Name == "after")
            .Where(it => it.Name == "before")
            .ExecuteCommandAsync();
        var namesAfterUpdate = await _client.Ado.SqlQueryAsync<string>(
            "SELECT Name FROM probe_query_filter ORDER BY TenantId");
        var deleteRows = await _client.Deleteable<ProbeTenantEntity>()
            .Where(it => it.Name == "after")
            .ExecuteCommandAsync();
        var remaining = await _client.Ado.GetIntAsync("SELECT COUNT(*) FROM probe_query_filter");

        Assert.Multiple(() =>
        {
            Assert.That(queryCount, Is.EqualTo(1), "Queryable 应命中租户过滤器");
            Assert.That(updateRows, Is.EqualTo(2), "实测：Updateable 不受 QueryFilter 约束，两租户行均被更新");
            Assert.That(namesAfterUpdate, Is.All.EqualTo("after"));
            Assert.That(deleteRows, Is.EqualTo(2), "实测：Deleteable 不受 QueryFilter 约束，两租户行均被删除");
            Assert.That(remaining, Is.Zero);
        });
    }

    /// <summary>
    /// V-05：CodeFirst.InitTables 对已存在表会重建结构——删除多余列、把列类型改回模型定义、补齐模型新增列。
    /// <para>实测结论：生产不可由应用自动 InitTables；结构变更必须走幂等迁移脚本 + 版本状态机。</para>
    /// </summary>
    [Test]
    public async Task V05_InitTables_DropsColumnsAndRewritesTypes()
    {
        _client.CodeFirst.InitTables<ProbeSchemaV1>();
        var initial = await ReadColumnsAsync("probe_codefirst");

        // 手工制造漂移：加一列、放大已有列类型
        await _client.Ado.ExecuteCommandAsync(
            "ALTER TABLE probe_codefirst ADD COLUMN ExtraCol VARCHAR(30) NULL");
        await _client.Ado.ExecuteCommandAsync(
            "ALTER TABLE probe_codefirst MODIFY COLUMN Name VARCHAR(200) NULL");

        _client.CodeFirst.InitTables<ProbeSchemaV1>();
        var afterSameModel = await ReadColumnsAsync("probe_codefirst");

        // 模型自身变化：V2 去掉 Age 列、Name 改为 VARCHAR(100)、新增 AddedInV2 列
        _client.CodeFirst.InitTables<ProbeSchemaV2>();
        var afterNewModel = await ReadColumnsAsync("probe_codefirst");

        foreach (var (stage, snapshot) in new[]
                 {
                     ("initial", initial), ("same-model", afterSameModel), ("new-model", afterNewModel)
                 })
        {
            TestContext.Progress.WriteLine($"[V-05] {stage}: "
                + string.Join(", ", snapshot.Select(pair => $"{pair.Key}={pair.Value}")));
        }

        Assert.Multiple(() =>
        {
            Assert.That(initial, Does.ContainKey("Age"));
            Assert.That(afterSameModel, Does.Not.ContainKey("ExtraCol"), "实测：手工新增列被 InitTables 删除");
            Assert.That(afterSameModel["Name"], Is.EqualTo("varchar(50)"), "实测：手工放大的列类型被改回模型定义");
            Assert.That(afterNewModel, Does.Not.ContainKey("Age"), "实测：模型移除的列在库里被删除");
            Assert.That(afterNewModel, Does.ContainKey("AddedInV2"), "实测：模型新增列被补上");
            Assert.That(afterNewModel["Name"], Is.EqualTo("varchar(100)"), "实测：模型列长度变更会改列类型");
        });
    }

    /// <summary>V-01 实验实体。</summary>
    [SugarTable("probe_query_filter")]
    public sealed class ProbeTenantEntity
    {
        /// <summary>主键。</summary>
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        /// <summary>租户 Id。</summary>
        public Guid? TenantId { get; set; }

        /// <summary>可变字段。</summary>
        [SugarColumn(Length = 50)]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>V-05 实验实体（初始模型）。</summary>
    [SugarTable("probe_codefirst")]
    public sealed class ProbeSchemaV1
    {
        /// <summary>主键。</summary>
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        /// <summary>名称列。</summary>
        [SugarColumn(Length = 50, IsNullable = true)]
        public string? Name { get; set; }

        /// <summary>年龄列（V2 模型中移除）。</summary>
        public int Age { get; set; }
    }

    /// <summary>V-05 实验实体（同表名的演进模型）。</summary>
    [SugarTable("probe_codefirst")]
    public sealed class ProbeSchemaV2
    {
        /// <summary>主键。</summary>
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        /// <summary>名称列（长度在代码中变更）。</summary>
        [SugarColumn(Length = 100, IsNullable = true)]
        public string? Name { get; set; }

        /// <summary>新增列。</summary>
        [SugarColumn(Length = 30, IsNullable = true)]
        public string? AddedInV2 { get; set; }
    }
}
