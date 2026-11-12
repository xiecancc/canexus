
using API.Authorization;
using API.Extensions;
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
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/monitor/cache")]
[Authorize]
public class SysCacheMonitorController(ISender sender) : ControllerBase
{
    /// <summary>获取缓存运行信息（连接状态 / 版本 / 内存 / 键空间命中率 / TTL 档位配置快照）。</summary>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>缓存运行信息快照。</returns>
    [HttpGet]
    [RequirePermission(PermissionDefinitions.MonitorCacheList)]
    public async Task<IActionResult> Cache(CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new CacheMonitorQuery(), cancellationToken)));
    }
}
