using SqlSugar;

namespace Infrastructure.DataAccess.Migrations;

/// <summary>
/// v002：操作日志审计字段扩展（T-13 迁移口径修订）——<c>sys_oper_log</c> 的
/// <c>UserId</c> / <c>TraceId</c> 两列随基线模型建立，本迁移为「纯登记型」版本：
/// 不携带 DDL 脚本、不再调用 <c>CodeFirst.InitTables</c>（生产禁止应用自动改表）。
/// <para>纯登记型迁移的指纹由 ID + 说明计算（正文为空）；有脚本的迁移（如 v001）指纹取脚本正文。</para>
/// </summary>
public sealed class AddOperLogAuditColumnsMigration : ISchemaMigration
{
    /// <inheritdoc />
    public string MigrationId => "v002";

    /// <inheritdoc />
    /// <remarks>说明文字保持不变：存量库记录的旧口径指纹（ID + 说明）需据此识别并一次性升级为正文指纹。</remarks>
    public string Description => "操作日志审计字段：sys_oper_log 新增 user_id（操作人用户 Id）与 trace_id（链路 TraceId）可空列，结构由 CodeFirst 同步";

    /// <inheritdoc />
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
