
using API.Authorization;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>缓存监控接口：Redis 服务器概要与缓存 TTL 配置快照（只读，供运维监控展示；未启用 Redis 时输出降级快照）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/monitor/cache")]
[Authorize]
public class SysCacheMonitorController(ISender sender) : ControllerBase
{
    /// <summary>获取缓存运行信息（连接状态 / 版本 / 内存 / 键空间命中率 / TTL 档位配置快照）。</summary>
    /// <returns>缓存运行信息快照。</returns>
    [HttpGet]
    [RequirePermission(PermissionDefinitions.MonitorCacheList)]
    public async Task<IActionResult> Cache()
    {
        return Ok(Result.Ok(await sender.Send(new CacheMonitorQuery())));
    }
}
