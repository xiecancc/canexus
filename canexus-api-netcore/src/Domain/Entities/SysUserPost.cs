
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 用户与岗位关联实体（关联表）：无独立主键，以 (UserId, PostId) 联合主键唯一定位；
/// 仅创建审计，无更新审计 / 租户 / 乐观并发语义，删除为物理删除。
/// </summary>
[SugarTable("sys_user_post", TableDescription = "用户与岗位关联实体（关联表）")]
public class SysUserPost : DomainBase
{
    /// <summary>用户 Id（联合主键之一）。</summary>
    [SugarColumn(ColumnDescription = "用户 Id（联合主键之一）。", IsPrimaryKey = true)]
    public Guid UserId { get; set; }

    /// <summary>岗位 Id（联合主键之一）。</summary>
    [SugarColumn(ColumnDescription = "岗位 Id（联合主键之一）。", IsPrimaryKey = true)]
    public Guid PostId { get; set; }
}
