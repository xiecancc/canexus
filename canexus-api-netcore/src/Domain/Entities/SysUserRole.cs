using SqlSugar;

namespace Domain.Entities;

/// <summary>
/// 用户与角色关联实体（关联表）：无独立主键，以 (UserId, RoleId) 联合主键唯一定位；
/// 仅创建审计，无更新审计 / 租户 / 乐观并发语义，删除为物理删除。
/// </summary>
[SugarTable("sys_user_role", TableDescription = "用户与角色关联实体（关联表）：无独立主键，以 (UserId, RoleId) 联合主键唯一定位； 仅创建审计，无更新审计 / 租户 / 乐观并发语义，删除为物理删除。")]
public class SysUserRole : DomainBase
{
    /// <summary>用户 Id（联合主键之一）。</summary>
    [SugarColumn(ColumnDescription = "用户 Id（联合主键之一）。", IsPrimaryKey = true)]
    public Guid UserId { get; set; }

    /// <summary>角色 Id（联合主键之一）。</summary>
    [SugarColumn(ColumnDescription = "角色 Id（联合主键之一）。", IsPrimaryKey = true)]
    public Guid RoleId { get; set; }
}
