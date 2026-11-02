using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Users;
/// <summary>用户分页查询请求（GET /api/system/user/page）。</summary>
public class UserPageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配用户名 / 昵称。</summary>
    public string? Keyword { get; set; }

    /// <summary>部门 Id 精确过滤。</summary>
    public Guid? DeptId { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>用户列表行（分页数据项）：直接继承聚合分页最小根；DeptName / Roles 由处理器组装。</summary>
public class UserPagedResponse : AggregatePagedResponse
{
    /// <summary>用户名。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    public string NickName { get; set; } = string.Empty;

    /// <summary>所属部门 Id。</summary>
    public Guid? DeptId { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    public string? Phone { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>部门名称（处理器组装）。</summary>
    public string? DeptName { get; set; }

    /// <summary>角色简项列表（处理器组装）。</summary>
    public List<UserRoleItemResponse> Roles { get; set; } = [];
}

/// <summary>用户角色简项。</summary>
public class UserRoleItemResponse : AggregateItemResponse
{
    /// <summary>角色 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;
}

/// <summary>用户详情（GET /api/system/user/{id}）：直接继承聚合详情根，业务字段平铺；RoleIds 由处理器组装。</summary>
public class UserDetailResponse : AggregateDetailResponse
{
    /// <summary>用户名。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    public string NickName { get; set; } = string.Empty;

    /// <summary>所属部门 Id。</summary>
    public Guid? DeptId { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    public string? Phone { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>已分配角色 Id 集合（处理器组装）。</summary>
    public List<Guid> RoleIds { get; set; } = [];
}

/// <summary>用户写入请求（POST / PUT 共用载荷；镜像 SysUser 聚合的启停 / 备注与业务字段；
/// UserName / Password 仅创建时可指定——更新时后端忽略：用户名创建后不可改，更新不修改密码）。</summary>
public class UserInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>用户昵称。</summary>
    public string NickName { get; set; } = string.Empty;

    /// <summary>所属部门 Id。</summary>
    public Guid? DeptId { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>手机号。</summary>
    public string? Phone { get; set; }

    /// <summary>角色 Id 集合（全删全插保存）。</summary>
    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>用户名（唯一，创建后不可改；仅创建生效）。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>初始密码（可空，缺省 123456；仅创建生效）。</summary>
    public string? Password { get; set; }
}

/// <summary>修改用户状态请求（PUT /api/system/user/{id}/status）。</summary>
public class UserStatusRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;
}

/// <summary>重置密码请求（PUT /api/system/user/{id}/password）。</summary>
public class UserPasswordRequest : AggregateInputRequest
{
    /// <summary>新密码。</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>分配角色请求（PUT /api/system/user/{id}/roles）。</summary>
public class UserRolesRequest : AggregateInputRequest
{
    /// <summary>角色 Id 集合（全删全插保存）。</summary>
    public List<Guid> RoleIds { get; set; } = [];
}
