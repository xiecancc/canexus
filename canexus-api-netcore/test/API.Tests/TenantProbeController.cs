using Domain.Entities;
using Domain.Services.Users;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// T2 取证探针（仅存在于测试程序集）：在真实 HTTP 请求上下文内回显租户上下文、租户校验结论
///（含「请求内清缓存后重新校验」口径）、认证状态，以及全局过滤器开启 / ClearFilter 关闭下 sys_tenant 的可见行数。
/// </summary>
[ApiController]
[Route("api/diagnostics")]
[AllowAnonymous]
public sealed class TenantProbeController(
    ITenantContext tenantContext,
    ITenantAccessValidator validator,
    IUserContext currentUser,
    ICache cache,
    ISqlSugarClient db) : ControllerBase
{
    /// <summary>回显当前请求的租户上下文、校验结论与可见行数。</summary>
    /// <param name="fresh">为 true 时先在请求内清除该租户的访问缓存再校验，模拟真正的冷缓存。</param>
    [HttpGet("tenant")]
    public async Task<IActionResult> GetAsync([FromQuery] bool fresh = false)
    {
        var tenantId = tenantContext.TenantId;
        TenantAccessStatus? cached = tenantId is { } id0 ? await validator.ValidateAsync(id0) : null;
        if (fresh && tenantId is { } id1)
        {
            await cache.RemoveAsync(TenantAccessValidator.CacheKeyPrefix + id1);
        }

        TenantAccessStatus? revalidated = tenantId is { } id2 ? await validator.ValidateAsync(id2) : null;
        var filteredCount = await db.Queryable<SysTenant>().CountAsync();
        var unfilteredCount = await db.Queryable<SysTenant>().ClearFilter<SysTenant>().CountAsync();
        return Ok(new
        {
            tenantId = tenantId?.ToString(),
            cached = cached?.ToString(),
            revalidated = revalidated?.ToString(),
            isAuthenticated = currentUser.IsAuthenticated,
            userTenantId = currentUser.TenantId?.ToString(),
            filteredCount,
            unfilteredCount
        });
    }
}
