
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>参数配置管理接口：分页、按键名取值、增删改与恢复（configKey 唯一；内置参数不可删除）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/config")]
[Authorize]
public class SysConfigController(ISender sender) : ControllerBase
{
    /// <summary>分页查询参数配置（keyword 模糊匹配名称 / 键名 / 键值）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemConfigList)]
    public async Task<IActionResult> Page([FromQuery] ConfigPageRequest request, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new ConfigQueryPaged(request), cancellationToken));
    }

    /// <summary>按参数键名查询参数值（走缓存，写操作后立即刷新）。</summary>
    /// <param name="configKey">参数键名。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>参数值字符串。</returns>
    [HttpGet("key/{configKey}")]
    [RequirePermission(PermissionDefinitions.SystemConfigQuery)]
    public async Task<IActionResult> GetByKey(string configKey, CancellationToken cancellationToken = default)
    {
        // 显式泛型参数：避免 string 实参被解析为非泛型 Ok(message) 重载导致 data 丢失
        return Ok(Result.Ok<string>(await sender.Send(new ConfigQueryByKey(configKey), cancellationToken)));
    }

    /// <summary>创建参数配置（configKey 唯一，创建项一律非内置）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示参数写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemConfigAdd)]
    [OperLog("参数配置", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] ConfigInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new ConfigCreateCommand(request), cancellationToken);
        return this.OkData(affected, "创建成功");
    }

    /// <summary>更新参数配置（键名可变更，新旧键缓存均失效；内置参数可编辑）。</summary>
    /// <param name="id">参数 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemConfigEdit)]
    [OperLog("参数配置", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] ConfigInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new ConfigUpdateCommand(id, version, request), cancellationToken);
        return this.OkData(affected, "更新成功");
    }

    /// <summary>删除参数配置（内置参数拒绝删除）。</summary>
    /// <param name="id">参数 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemConfigRemove)]
    [OperLog("参数配置", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new ConfigDeleteCommand(id, version), cancellationToken);
        return this.OkData(affected, "删除成功");
    }

    /// <summary>恢复参数配置（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">参数 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemConfigRestore)]
    [OperLog("参数配置", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new ConfigRestoreCommand(id, version), cancellationToken);
        return this.OkData(restored, message: restored ? "恢复成功" : "参数不存在或未删除，无需恢复");
    }
}
