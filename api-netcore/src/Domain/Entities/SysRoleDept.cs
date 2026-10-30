namespace Domain.Entities;

using SqlSugar;

/// <summary>
/// 角色与部门关联实体（关联表，角色数据范围为自定义 Custom 时的部门授权明细）：
/// 无独立主键，以 (RoleId, DeptId) 联合主键唯一定位；
/// 仅创建审计，无更新审计 / 租户 / 乐观并发语义，删除为物理删除。
/// </summary>
[SugarTable("sys_role_dept")]
public class SysRoleDept : DomainBase
{
    /// <summary>角色 Id（联合主键之一）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public Guid RoleId { get; set; }

    /// <summary>部门 Id（联合主键之一）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public Guid DeptId { get; set; }
}
