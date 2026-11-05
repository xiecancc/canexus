
using SqlSugar;

namespace Domain.Entities;

/// <summary>
/// 数据库迁移记录实体（P1-A3）：记录已应用的基线 / 增量迁移，支撑变更审计、幂等应用与启动校验。
/// <para>基线（baseline-v1）登记 CodeFirst 模型现状的全部表名清单（结构仍由 CodeFirst 同步，
/// 迁移器不重建表）；增量迁移由 <c>ISchemaMigration</c> 定义、按 MigrationId 顺序应用，
/// 应用与记录写入同事务（失败整体回滚，生产可回滚）。</para>
/// </summary>
[SugarTable("sys_schema_migration", TableDescription = "数据库迁移记录实体：记录已应用的基线 / 增量迁移，支撑变更审计、幂等应用与启动校验。")]
[SugarIndex("uk_sys_schema_migration_migrationid", nameof(MigrationId), OrderByType.Asc, true)]
public sealed class SchemaMigrationRecord : AggregateBase
{
    /// <summary>迁移 ID（基线为 baseline-v1，增量按语义序号如 v001），全库唯一。</summary>
    [SugarColumn(ColumnDescription = "迁移 ID（基线为 baseline-v1，增量按语义序号如 v001），全库唯一。", Length = 64)]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>迁移说明（审计口径，如 "用户表新增登录失败计数字段"）。</summary>
    [SugarColumn(ColumnDescription = "迁移说明（审计口径，如 '用户表新增登录失败计数字段'）。", Length = 255, IsNullable = true)]
    public string? MigrationDescription { get; set; }

    /// <summary>迁移脚本指纹（SHA-256，防篡改审计）；null 表示未计算。</summary>
    [SugarColumn(ColumnDescription = "迁移脚本指纹（SHA-256，防篡改审计）；null 表示未计算。", Length = 64, IsNullable = true)]
    public string? ScriptHash { get; set; }

    /// <summary>本迁移登记 / 创建的表名清单（逗号分隔；基线为模型全表，增量为其建表）。</summary>
    [SugarColumn(ColumnDescription = "本迁移登记 / 创建的表名清单（逗号分隔；基线为模型全表，增量为其建表）。", ColumnDataType = "longtext", IsNullable = true)]
    public string? TableNames { get; set; }

    /// <summary>应用时间（审计口径，服务器本地时间）。</summary>
    [SugarColumn(ColumnDescription = "应用时间（审计口径，服务器本地时间）。")]
    public DateTime AppliedAt { get; set; }

    /// <summary>创建基线记录：登记当前模型全部表名（排序后拼接）与指纹。</summary>
    /// <param name="tableNames">模型表名清单（已排序去重）。</param>
    /// <param name="scriptHash">基线指纹。</param>
    /// <returns>基线记录。</returns>
    public static SchemaMigrationRecord CreateBaseline(IReadOnlyList<string> tableNames, string scriptHash)
    {
        return new SchemaMigrationRecord
        {
            MigrationId = "baseline-v1",
            MigrationDescription = "CodeFirst 模型基线：登记当前模型全部表（结构由 CodeFirst 同步，不重建表）",
            ScriptHash = scriptHash,
            TableNames = string.Join(",", tableNames),
            AppliedAt = DateTime.UtcNow
        };
    }

    /// <summary>创建增量迁移记录。</summary>
    /// <param name="migrationId">迁移 ID。</param>
    /// <param name="description">迁移说明。</param>
    /// <param name="scriptHash">迁移指纹。</param>
    /// <param name="createdTableNames">迁移创建的表名清单（供启动校验比对）。</param>
    /// <returns>增量记录。</returns>
    public static SchemaMigrationRecord CreateIncremental(
        string migrationId,
        string description,
        string scriptHash,
        IReadOnlyList<string> createdTableNames)
    {
        return new SchemaMigrationRecord
        {
            MigrationId = migrationId,
            MigrationDescription = description,
            ScriptHash = scriptHash,
            TableNames = string.Join(",", createdTableNames.OrderBy(name => name, StringComparer.Ordinal)),
            AppliedAt = DateTime.UtcNow
        };
    }
}
