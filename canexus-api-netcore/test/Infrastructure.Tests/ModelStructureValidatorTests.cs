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
/// 并用真实临时库验证「绕过 CodeFirst / 迁移通道删列」会被识别。
/// <para>P2-6 追加：列类型 / 长度的确定性规则（长度不足、类型族明显不一致、decimal 精度 / 小数位）
/// 与模型声明索引的存在性比对，并覆盖 MySQL 类型别名、未声明长度列、索引名不一致等不误报场景。</para>
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

    /// <summary>P2-6：模型声明长度大于数据库实际字符长度时必须报告。</summary>
    [Test]
    public void Compare_LengthShortage_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Name", false, Length: 50, IsText: true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Name", false, "varchar", "varchar(20)", CharacterMaximumLength: 20)]);

        Assert.That(problems, Has.Some.Contains("列 Name 长度不足"));
    }

    /// <summary>P2-6：数据库列长度等于或大于模型声明时不误报。</summary>
    [Test]
    public void Compare_LengthEqualOrLarger_NoFalsePositive()
    {
        foreach (var actualLength in new long[] { 50, 100 })
        {
            var problems = CompareSingle(
                [new ModelStructureValidator.ModelColumn("Name", false, Length: 50, IsText: true)],
                [new ModelStructureValidator.DatabaseColumn("t", "Name", false, "varchar", $"varchar({actualLength})", CharacterMaximumLength: actualLength)]);

            Assert.That(problems, Is.Empty, $"数据库长度 {actualLength} ≥ 模型 50 不应报差异");
        }
    }

    /// <summary>P2-6：MySQL 类型别名 int(11) / tinyint(1) / bigint(20) 不因列类型文本不同误报。</summary>
    [Test]
    public void Compare_MySqlTypeAliases_NoFalsePositive()
    {
        var problems = CompareSingle(
            [
                new ModelStructureValidator.ModelColumn("Id", false, IsNumeric: true),
                new ModelStructureValidator.ModelColumn("Count", false, IsNumeric: true),
                new ModelStructureValidator.ModelColumn("Enabled", false, IsNumeric: true)
            ],
            [
                new ModelStructureValidator.DatabaseColumn("t", "Id", false, "bigint", "bigint(20)", NumericPrecision: 19),
                new ModelStructureValidator.DatabaseColumn("t", "Count", false, "int", "int(11)", NumericPrecision: 10),
                new ModelStructureValidator.DatabaseColumn("t", "Enabled", false, "tinyint", "tinyint(1)", NumericPrecision: 3)
            ]);

        Assert.That(problems, Is.Empty);
    }

    /// <summary>P2-6：模型字符串列在数据库中被建成数值列时报告类型族明显不一致。</summary>
    [Test]
    public void Compare_TextColumnAsNumericDb_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Name", false, Length: 50, IsText: true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Name", false, "int", "int(11)", NumericPrecision: 10)]);

        Assert.That(problems, Has.Some.Contains("列 Name 类型族不一致"));
    }

    /// <summary>P2-6：模型数值列在数据库中被建成字符列时报告类型族明显不一致。</summary>
    [Test]
    public void Compare_NumericColumnAsTextDb_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Count", false, IsNumeric: true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Count", false, "varchar", "varchar(50)", CharacterMaximumLength: 50)]);

        Assert.That(problems, Has.Some.Contains("列 Count 类型族不一致"));
    }

    /// <summary>P2-6：decimal 精度与小数位同时不足时两类差异均报告。</summary>
    [Test]
    public void Compare_DecimalPrecisionAndScale_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Amount", false, Length: 18, DecimalDigits: 4, IsNumeric: true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Amount", false, "decimal", "decimal(10,2)", NumericPrecision: 10, NumericScale: 2)]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("列 Amount 长度不足"));
            Assert.That(problems, Has.Some.Contains("列 Amount 小数位不足"));
        });
    }

    /// <summary>P2-6：模型未声明长度（Length=0）的列一律跳过长度 / 类型比对，避免误报。</summary>
    [Test]
    public void Compare_UnmarkedLengthColumn_NoFalsePositive()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Remark", true, IsText: true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Remark", true, "varchar", "varchar(10)", CharacterMaximumLength: 10)]);

        Assert.That(problems, Is.Empty);
    }

    /// <summary>P2-6：模型声明的唯一索引在数据库中缺失时报告。</summary>
    [Test]
    public void Compare_MissingUniqueIndex_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Id", false), new ModelStructureValidator.ModelColumn("Code", false)],
            [new ModelStructureValidator.DatabaseColumn("t", "Id", false), new ModelStructureValidator.DatabaseColumn("t", "Code", false)],
            [new ModelStructureValidator.ModelIndex("Code", "uniq_code", IsUnique: true)]);

        Assert.That(problems, Has.Some.Contains("列 Code 缺少唯一索引"));
    }

    /// <summary>P2-6：模型声明的普通索引在数据库中缺失时报告。</summary>
    [Test]
    public void Compare_MissingNormalIndex_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Id", false), new ModelStructureValidator.ModelColumn("Name", true)],
            [new ModelStructureValidator.DatabaseColumn("t", "Id", false), new ModelStructureValidator.DatabaseColumn("t", "Name", true)],
            [new ModelStructureValidator.ModelIndex("Name", "idx_name", IsUnique: false)]);

        Assert.That(problems, Has.Some.Contains("列 Name 缺少普通索引"));
    }

    /// <summary>P2-6：模型声明唯一索引但该列仅被非唯一索引覆盖时仍报告。</summary>
    [Test]
    public void Compare_UniqueIndexBuiltAsNonUnique_IsReported()
    {
        var problems = CompareSingle(
            [new ModelStructureValidator.ModelColumn("Id", false), new ModelStructureValidator.ModelColumn("Code", false)],
            [new ModelStructureValidator.DatabaseColumn("t", "Id", false), new ModelStructureValidator.DatabaseColumn("t", "Code", false)],
            [new ModelStructureValidator.ModelIndex("Code", "uniq_code", IsUnique: true)],
            [new ModelStructureValidator.DatabaseIndex("t", "idx_code", "Code", NonUnique: true)]);

        Assert.That(problems, Has.Some.Contains("列 Code 缺少唯一索引"));
    }

    /// <summary>P2-6：索引按「列是否被覆盖」判定，实际索引名与模型组名不同不误报。</summary>
    [Test]
    public void Compare_IndexesCoveredByAnyName_NoFalsePositive()
    {
        var problems = CompareSingle(
            [
                new ModelStructureValidator.ModelColumn("Id", false),
                new ModelStructureValidator.ModelColumn("Code", false),
                new ModelStructureValidator.ModelColumn("Name", true)
            ],
            [
                new ModelStructureValidator.DatabaseColumn("t", "Id", false),
                new ModelStructureValidator.DatabaseColumn("t", "Code", false),
                new ModelStructureValidator.DatabaseColumn("t", "Name", true)
            ],
            [
                new ModelStructureValidator.ModelIndex("Code", "uniq_code", IsUnique: true),
                new ModelStructureValidator.ModelIndex("Name", "idx_name", IsUnique: false)
            ],
            [
                new ModelStructureValidator.DatabaseIndex("t", "UQ_TBL_CODE", "Code", NonUnique: false),
                new ModelStructureValidator.DatabaseIndex("t", "IX_OTHER", "Name", NonUnique: true)
            ]);

        Assert.That(problems, Is.Empty);
    }

    /// <summary>P2-6：CollectModel 全部取 ORM 元数据（不手写列名清单）；列与索引均由实体声明驱动。</summary>
    [Test]
    public void CollectModel_ReadsOrmMetadata_NoHardcodedColumns()
    {
        using var client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = "server=localhost;database=probe;uid=root;pwd=x",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        var validator = new ModelStructureValidator(client, NullLogger<ModelStructureValidator>.Instance);

        var table = validator.CollectModel([typeof(SysJob)]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(table.Name, Is.EqualTo("sys_job"));
            Assert.That(table.Columns.Any(column => column.Name == "JobName" && column.Length == 50 && column.IsText), Is.True);
            Assert.That(table.Columns.Any(column => column.Name == "ParametersJson" && column.Length == 2000 && column.IsText), Is.True);
            Assert.That(table.Indexes?.Any(index => index.Name == "ix_sys_job_scope" && index.ColumnName == "DeletedAt") == true,
                Is.True, "模型侧索引清单应取自实体 SugarIndex 声明（ORM 元数据驱动，非硬编码）");
        });
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

    /// <summary>单表比对辅助：固定补一条 PRIMARY 索引，避免缺主键差异干扰列 / 索引规则的断言。</summary>
    /// <param name="modelColumns">模型列。</param>
    /// <param name="dbColumns">数据库列。</param>
    /// <param name="modelIndexes">模型声明索引（可选）。</param>
    /// <param name="dbIndexes">附加数据库索引行（可选）。</param>
    /// <returns>差异列表。</returns>
    private static IReadOnlyList<string> CompareSingle(
        ModelStructureValidator.ModelColumn[] modelColumns,
        ModelStructureValidator.DatabaseColumn[] dbColumns,
        ModelStructureValidator.ModelIndex[]? modelIndexes = null,
        ModelStructureValidator.DatabaseIndex[]? dbIndexes = null)
    {
        var model = new[]
        {
            new ModelStructureValidator.ModelTable(
                "t", modelColumns, modelIndexes ?? [])
        };
        var indexes = new List<ModelStructureValidator.DatabaseIndex>
        {
            new("t", "PRIMARY", "Id", NonUnique: false)
        };
        if (dbIndexes is not null)
        {
            indexes.AddRange(dbIndexes);
        }

        return ModelStructureValidator.Compare(model, dbColumns, indexes);
    }
}
