
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库增量迁移定义（P1-A3）：以 MigrationId 唯一定义一次结构变更，
/// 由 <see cref="SchemaMigrator"/> 按 ID 顺序在事务内应用（应用脚本与记录写入同事务，失败整体回滚）。
/// <para>扩展约定：新增迁移实现本接口并注册进迁移清单；<see cref="CreatedTableNames"/> 声明本次建表，
/// 供启动校验比对（模型新增表但未登记迁移时启动失败，避免只改实体不补迁移）。</para>
/// </summary>
public interface ISchemaMigration
{
    /// <summary>迁移 ID：全库唯一，按语义序号命名（如 v001、v002），顺序应用。</summary>
    public string MigrationId { get; }

    /// <summary>迁移说明（审计口径）。</summary>
    public string Description { get; }

    /// <summary>本次迁移创建的表名清单（无则空）。</summary>
    public IReadOnlyList<string> CreatedTableNames => Array.Empty<string>();

    /// <summary>
    /// 迁移脚本正文（幂等 SQL，逐条在事务内执行）。脚本指纹由正文计算，正文变更在启动期被发现；
    /// 纯登记型迁移（历史基线由 CodeFirst 建表）返回空清单。
    /// </summary>
    public IReadOnlyList<string> Scripts => Array.Empty<string>();

    /// <summary>应用迁移脚本（在事务内执行，失败整体回滚；原生 SQL 必须经 <paramref name="sql"/> 执行器运行，
    /// 裸 Ado.ExecuteCommand 不挂接事务，会自动提交）。</summary>
    /// <param name="client">SqlSugar 客户端（ORM 操作与事务同连接）。</param>
    /// <param name="sql">事务内 SQL 执行器（显式挂接当前事务）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken);
}
