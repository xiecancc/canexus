using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Roles;
/// <summary>角色分页查询请求（GET /api/system/role/page）。</summary>
public class RolePageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配角色名称 / 角色权限字符。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>角色列表行（分页数据项）：直接继承聚合分页最小根，承载列表展示业务字段。</summary>
public class RolePagedResponse : AggregatePagedResponse
{
    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符（roleKey）。</summary>
    public string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（见 <see cref="Domain.Shared.Enums.DataScope"/>）。</summary>
    public DataScope DataScope { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}

/// <summary>角色下拉查询请求：角色下拉查询载荷（预留为后续筛选条件的挂载点）。</summary>
public class RoleDropdownRequest : AggregateDropdownRequest
{
}

/// <summary>角色下拉简项（GET /api/system/role/all，用于用户分配角色）。</summary>
public class RoleDropdownResponse : AggregateDropdownResponse
{
    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符（roleKey）。</summary>
    public string RoleKey { get; set; } = string.Empty;
}

/// <summary>角色详情（GET /api/system/role/{id}）：直接继承聚合详情根，业务字段平铺；含授权菜单 Id 集合。</summary>
public class RoleDetailResponse : AggregateDetailResponse
{
    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符（roleKey）。</summary>
    public string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（见 <see cref="Domain.Shared.Enums.DataScope"/>）。</summary>
    public DataScope DataScope { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>授权菜单 Id 集合（含目录 / 菜单 / 按钮）。</summary>
    public List<Guid> MenuIds { get; set; } = [];
}

/// <summary>角色写入请求（POST / PUT 共用载荷；镜像 SysRole 聚合的启停 / 备注与业务字段）。</summary>
public class RoleInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符（唯一；super_admin 为内置保留值，不可使用）。</summary>
    public string RoleKey { get; set; } = string.Empty;

    /// <summary>数据范围（见 <see cref="Domain.Shared.Enums.DataScope"/>）。</summary>
    public DataScope DataScope { get; set; } = DataScope.All;

    /// <summary>授权菜单 Id 集合（可为空，全删全插保存）。</summary>
    public List<Guid> MenuIds { get; set; } = [];
}


