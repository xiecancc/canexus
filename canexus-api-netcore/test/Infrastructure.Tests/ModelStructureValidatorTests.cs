using System.Text.RegularExpressions;
using Domain.Entities;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using NUnit.Framework;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// T5 #13 回归：模型快照与 information_schema 比对须能发现缺表、缺列、非空约束漂移与主键缺失；
/// 并用真实临时库验证「绕过迁移通道删列」会被识别（脚本指纹校验看不到这一类改动）。
/// </summary>
[TestFixture]
public class ModelStructureValidatorTests
{
    private static readonly ModelStructureValidator.ModelTable[] SampleModel =
    [
        new("sys_sample",
        [
            new ModelStructureValidator.ModelColumn("Id", false),
            new ModelStructureValidator.ModelColumn("Name", false),
            new ModelStructureValidator.ModelColumn("Remark", true)
        ])
    ];

    private static readonly ModelStructureValidator.DatabaseColumn[] SampleColumns =
    [
        new("sys_sample", "Id", false),
        new("sys_sample", "Name", false),
        new("sys_sample", "Remark", true)
    ];

    private static readonly ModelStructureValidator.DatabaseIndex[] SampleIndexes =
    [
        new("sys_sample", "PRIMARY", "Id")
    ];

    /// <summary>完全一致时无差异；表名 / 列名大小写不敏感（MySQL 列名比较口径）。</summary>
    [Test]
    public void Compare_ExactMatch_ReturnsNoProblems()
    {
        var problems = ModelStructureValidator.Compare(
            SampleModel,
            [.. SampleColumns.Select(column => new ModelStructureValidator.DatabaseColumn(
                column.TableName.ToUpperInvariant(), column.ColumnName.ToLowerInvariant(), column.IsNullable))],
            [.. SampleIndexes.Select(index => new ModelStructureValidator.DatabaseIndex(
                index.TableName.ToUpperInvariant(), index.IndexName, index.ColumnName))]);

        Assert.That(problems, Is.Empty);
    }

    /// <summary>四类差异均能被定位，且描述包含表名与列名。</summary>
    [Test]
    public void Compare_DetectsMissingTableColumnNullabilityAndPrimary()
    {
        var problems = ModelStructureValidator.Compare(
            SampleModel,
            [new ModelStructureValidator.DatabaseColumn("sys_sample", "Id", false)],
            Array.Empty<ModelStructureValidator.DatabaseIndex>());

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("缺少模型列 Name"));
            Assert.That(problems, Has.Some.Contains("缺少模型列 Remark"));
            Assert.That(problems, Has.Some.Contains("缺少 PRIMARY 索引"));
        });
    }

    /// <summary>模型表在 information_schema 完全无列记录时按「缺表」报告。</summary>
    [Test]
    public void Compare_MissingTable_IsReported()
    {
        var problems = ModelStructureValidator.Compare(
            SampleModel,
            Array.Empty<ModelStructureValidator.DatabaseColumn>(),
            Array.Empty<ModelStructureValidator.DatabaseIndex>());

        Assert.That(problems, Has.Some.Contains("表 sys_sample 在数据库中不存在"));
    }

    /// <summary>非空约束漂移（模型非空 / 数据库可空）必须报告。</summary>
    [Test]
    public void Compare_NullabilityMismatch_IsReported()
    {
        var problems = ModelStructureValidator.Compare(
            SampleModel,
            [.. SampleColumns.Select(column => column.ColumnName == "Name"
                ? column with { IsNullable = true }
                : column)],
            SampleIndexes);

        Assert.That(problems, Has.Some.Contains("列 Name 非空约束不一致"));
    }

    /// <summary>真实库验证：CodeFirst 建表后绕过迁移直接删列，模型快照比对必须报出该列。</summary>
    [Test]
    public async Task ValidateAsync_DetectsColumnDroppedOutsideMigrationChannel()
    {
        var source = TestDatabaseSupport.TestConnectionString;
        var database = $"super_admin_structure_{Guid.NewGuid():N}";
        var connectionString = Regex.Replace(source, "(?i)database=[^;]+", $"database={database}");
        var adminConnection = Regex.Replace(source, "(?i)database=[^;]+", "database=super_admin_test");

        await using (var connection = new MySqlConnection(adminConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{database}`";
            await command.ExecuteNonQueryAsync();
        }

        using var client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });

        try
        {
            client.CodeFirst.InitTables(typeof(SysJobLog));
            // 绕过迁移通道直接删列：脚本正文指纹不会变化，只能靠 information_schema 结构比对发现
            await client.Ado.ExecuteCommandAsync("ALTER TABLE `sys_job_log` DROP COLUMN `exception`");

            var validator = new ModelStructureValidator(client, NullLogger<ModelStructureValidator>.Instance);
            var (columns, indexes) = await validator.CollectDatabaseAsync();
            var problems = ModelStructureValidator.Compare(
                validator.CollectModel([typeof(SysJobLog)]), columns, indexes);

            Assert.That(problems, Has.Some.Contains("缺少模型列"),
                "绕过迁移通道删除的列必须被模型结构比对发现");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await validator.ValidateAsync([typeof(SysJobLog)]),
                "开启校验后结构不一致必须阻止宿主启动");
        }
        finally
        {
            await using var connection = new MySqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{database}`";
            await command.ExecuteNonQueryAsync();
        }
    }
}
