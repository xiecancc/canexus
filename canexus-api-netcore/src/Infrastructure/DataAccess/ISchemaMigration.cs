
using SqlSugar;

namespace Infrastructure.DataAccess;

/// <summary>
/// 数据库结构迁移定义：一次结构变更由 <see cref="MigrationId"/> 唯一标识，按语义序号排序执行。
/// <para>框架处于开发初期，数据库均为临时库，结构可直接删除重建并由 CodeFirst 依实体模型生成；
/// 迁移只承载 CodeFirst 无法表达的结构变更（索引、约束、数据订正等）。
/// <b>脚本必须幂等</b>：框架不记录任何迁移历史，每次启动都会按序执行清单中的全部迁移。</para>
/// <para>扩展方式：新增实现类放入 <c>Infrastructure.DataAccess.Migrations</c> 命名空间，
/// 并在依赖注入中注册为 <c>ISchemaMigration</c> 单例即可，无需登记基线或历史记录。</para>
/// </summary>
public interface ISchemaMigration
{
    /// <summary>迁移 ID：按 vNNN 语义序号命名（如 v001、v002），决定执行顺序且全清单唯一。</summary>
    public string MigrationId { get; }

    /// <summary>迁移说明（写入启动日志，便于定位结构变更）。</summary>
    public string Description { get; }

    /// <summary>幂等 SQL 脚本正文（逐条在同一事务内执行）；无脚本时返回空清单，仅使用 <see cref="ApplyAsync"/> 扩展点。</summary>
    public IReadOnlyList<string> Scripts => Array.Empty<string>();

    /// <summary>应用迁移（脚本执行完后调用；需要 ORM 协同的迁移可在此扩展）。
    /// <para>原生 SQL 必须经 <paramref name="sql"/> 执行器运行，裸 <c>Ado.ExecuteCommand</c> 不挂接事务，会自动提交。</para></summary>
    /// <param name="client">SqlSugar 客户端（与事务同连接）。</param>
    /// <param name="sql">事务内 SQL 执行器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken);
}
