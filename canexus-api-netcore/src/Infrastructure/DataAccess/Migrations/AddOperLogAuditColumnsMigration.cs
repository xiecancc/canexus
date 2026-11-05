using Domain.Entities;
using SqlSugar;

namespace Infrastructure.DataAccess.Migrations;
/// <summary>
/// v002：操作日志审计字段扩展（P0 默认审计）——<c>sys_oper_log</c> 新增
/// <c>user_id</c>（操作人用户 Id）与 <c>trace_id</c>（请求链路 TraceId）两个可空列。
/// <para>列结构由 CodeFirst 统一同步（启动流程先 InitTables 再应用增量），本迁移按治理约束登记
/// 「列由本次版本引入」；存量库在 CodeFirst 同步后本迁移为登记动作，防御性 InitTables 兜底极端场景。</para>
/// </summary>
public sealed class AddOperLogAuditColumnsMigration : ISchemaMigration
{
    /// <inheritdoc />
    public string MigrationId => "v002";

    /// <inheritdoc />
    public string Description => "操作日志审计字段：sys_oper_log 新增 user_id（操作人用户 Id）与 trace_id（链路 TraceId）可空列，结构由 CodeFirst 同步";

    /// <inheritdoc />
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
    {
        // 防御性结构同步：与 CodeFirst 启动同步保持同源（存量列幂等；可空新增列不锁长表）
        client.CodeFirst.InitTables(typeof(SysOperLog));
        return Task.CompletedTask;
    }
}
