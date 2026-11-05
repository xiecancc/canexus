
using Domain.Entities;
using SqlSugar;

namespace Infrastructure.DataAccess.Migrations;
/// <summary>
/// v001：岗位管理模块建表登记（P2）——新增 <c>sys_post</c>（岗位）与 <c>sys_user_post</c>（用户-岗位关联）。
/// <para>表结构由 CodeFirst 统一同步（启动流程先 InitTables 再应用增量），本迁移负责登记
/// 「这两张表由本次版本引入」，满足启动校验「模型新增表必须登记迁移」的治理约束；
/// 对存量库而言本迁移为登记动作（CodeFirst 已先行建表），对全新库同样成立。</para>
/// </summary>
public sealed class AddPostTablesMigration : ISchemaMigration
{
    /// <inheritdoc />
    public string MigrationId => "v001";

    /// <inheritdoc />
    public string Description => "岗位管理模块建表：sys_post（岗位）与 sys_user_post（用户-岗位关联），结构由 CodeFirst 同步";

    /// <inheritdoc />
    public IReadOnlyList<string> CreatedTableNames { get; } = ["sys_post", "sys_user_post"];

    /// <inheritdoc />
    public Task ApplyAsync(ISqlSugarClient client, ITransactionSqlRunner sql, CancellationToken cancellationToken)
    {
        // 表结构由 CodeFirst 统一建表 / 同步（见 DbInitializer 流程次序），无额外脚本；
        // 保留防御性建表：极端场景（迁移记录存在但表被手工删除）下恢复表结构
        client.CodeFirst.InitTables(typeof(SysPost), typeof(SysUserPost));
        return Task.CompletedTask;
    }
}
