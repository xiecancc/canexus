namespace Infrastructure.Users;

using System.Security.Claims;
using Domain.Services.Users;
using Microsoft.AspNetCore.Http;

/// <summary>
/// 基于 HttpContext 的当前用户上下文实现：从 JWT claims 解析用户 / 部门 / 角色 / 租户信息。
/// <para>注册为单例（依赖 IHttpContextAccessor），请求间天然隔离；无请求上下文（后台任务等）时各成员返回 null。</para>
/// </summary>
public sealed class HttpContextUserContext : UserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>构造函数。</summary>
    /// <param name="httpContextAccessor">HTTP 上下文访问器。</param>
    public HttpContextUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var userId) ? userId : null;

    /// <inheritdoc />
    public string? UserName =>
        Principal?.FindFirst("unique_name")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? Principal?.FindFirst("userName")?.Value;

    /// <inheritdoc />
    public Guid? DeptId =>
        Guid.TryParse(Principal?.FindFirst("deptId")?.Value, out var deptId) ? deptId : null;

    /// <inheritdoc />
    public IReadOnlyList<Guid> RoleIds =>
        (Principal?.FindAll("roleId") ?? [])
            .Select(claim => Guid.TryParse(claim.Value, out var roleId) ? roleId : Guid.Empty)
            .Where(roleId => roleId != Guid.Empty)
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Roles =>
        (Principal?.FindAll("role") ?? []).Select(claim => claim.Value).ToList();

    /// <inheritdoc />
    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirst("tenantId")?.Value, out var tenantId) ? tenantId : null;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public bool IsSuperAdmin => Roles.Contains("super_admin");
}
