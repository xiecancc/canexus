
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 角色与菜单关联实体（关联表）：无独立主键，以 (RoleId, MenuId) 联合主键唯一定位；
/// 仅创建审计，无更新审计 / 租户 / 乐观并发语义，删除为物理删除。
/// </summary>
[SugarTable("sys_role_menu")]
public class SysRoleMenu : DomainBase
{
    /// <summary>角色 Id（联合主键之一）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public Guid RoleId { get; set; }

    /// <summary>菜单 Id（联合主键之一）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public Guid MenuId { get; set; }
}
