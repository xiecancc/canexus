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

/// <summary>角色响应公共基类：对齐 SysRole 聚合的业务字段（Id / Version / CreatedAt 由 <see cref="AggregatePagedResponse"/> 承载）。</summary>
public abstract class RoleBaseResponse : AggregatePagedResponse
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

/// <summary>角色列表行（分页数据项）。</summary>
public class RoleListResponse : RoleBaseResponse
{
}

/// <summary>角色下拉简项（GET /api/system/role/all，用于用户分配角色）。</summary>
public class RoleSimpleResponse : AggregateListResponse
{
    /// <summary>角色 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色权限字符（roleKey）。</summary>
    public string RoleKey { get; set; } = string.Empty;
}

/// <summary>角色详情（GET /api/system/role/{id}，含授权菜单 Id 集合）。</summary>
public class RoleDetailResponse : RoleBaseResponse
{
    /// <summary>授权菜单 Id 集合（含目录 / 菜单 / 按钮）。</summary>
    public List<Guid> MenuIds { get; set; } = [];
}

/// <summary>角色写入请求基类：创建 / 更新共同字段（镜像 SysRole 聚合的启停 / 备注与业务字段）。</summary>
public abstract class RoleRequest : AggregateChangeRequest
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

/// <summary>创建角色请求（POST /api/system/role）。</summary>
public class RoleCreateRequest : RoleRequest
{
}

/// <summary>更新角色请求（PUT /api/system/role/{id}；内置 super_admin 角色整体拒绝编辑；Version 须回传乐观锁版本号）。</summary>
public class RoleUpdateRequest : RoleRequest
{
}

/// <summary>删除角色请求（DELETE /api/system/role/{id}；Version 须回传乐观锁版本号）。</summary>
public class RoleDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
}

/// <summary>角色列表查询请求（全量启用角色下拉；预留为后续筛选条件的挂载点）。</summary>
public class RoleListRequest : AggregateListRequest
{
}
