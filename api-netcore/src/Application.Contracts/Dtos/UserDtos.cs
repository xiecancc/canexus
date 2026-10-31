
using Application.Contracts.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Users;
/// <summary>用户分页查询请求（GET /api/system/user/page）。</summary>
public class UserPageRequest : PagedRequest
{
    /// <summary>关键字：模糊匹配用户名 / 昵称。</summary>
    public string? Keyword { get; set; }

    /// <summary>部门 Id 精确过滤。</summary>
    public Guid? DeptId { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>用户 VO 公共基类：对齐 SysUser 聚合的业务字段。</summary>
public abstract class UserBaseVO
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
}

/// <summary>用户列表行（分页数据项；DeptName / Roles 由处理器组装）。</summary>
public class UserListVO : UserBaseVO
{
    /// <summary>用户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>部门名称（处理器组装）。</summary>
    public string? DeptName { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>角色简项列表（处理器组装）。</summary>
    public List<UserRoleItemVO> Roles { get; set; } = [];
}

/// <summary>用户角色简项。</summary>
public class UserRoleItemVO
{
    /// <summary>角色 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>角色名称。</summary>
    public string RoleName { get; set; } = string.Empty;
}

/// <summary>用户详情（GET /api/system/user/{id}；RoleIds 由处理器组装）。</summary>
public class UserDetailVO : UserBaseVO
{
    /// <summary>用户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>已分配角色 Id 集合（处理器组装）。</summary>
    public List<Guid> RoleIds { get; set; } = [];
}

/// <summary>用户写入请求基类：创建 / 更新共同字段（对齐 SysUser 聚合；UserName / Password 仅创建时可指定）。</summary>
public abstract class UserWriteRequest : StatusDescriptionRequest
{
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
}

/// <summary>创建用户请求（POST /api/system/user；密码缺省 123456）。</summary>
public class UserCreateRequest : UserWriteRequest
{
    /// <summary>用户名（唯一，创建后不可改）。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>初始密码（可空，缺省 123456）。</summary>
    public string? Password { get; set; }
}

/// <summary>更新用户请求（PUT /api/system/user/{id}；内置 admin 拒绝编辑）。</summary>
public class UserUpdateRequest : UserWriteRequest
{
}

/// <summary>修改用户状态请求（PUT /api/system/user/{id}/status）。</summary>
public class UserStatusRequest : StatusRequest
{
}

/// <summary>重置密码请求（PUT /api/system/user/{id}/password）。</summary>
public class UserPasswordRequest
{
    /// <summary>新密码。</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>分配角色请求（PUT /api/system/user/{id}/roles）。</summary>
public class UserRolesRequest
{
    /// <summary>角色 Id 集合（全删全插保存）。</summary>
    public List<Guid> RoleIds { get; set; } = [];
}
