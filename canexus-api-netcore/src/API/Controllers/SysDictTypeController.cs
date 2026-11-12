
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>字典类型管理接口：分页、增删改与恢复（dictType 唯一且不可变更）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/dict-type")]
[Authorize]
public class SysDictTypeController(ISender sender) : ControllerBase
{
    /// <summary>分页查询字典类型（keyword 模糊匹配字典名称 / 字典类型）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemDictList)]
    public async Task<IActionResult> Page([FromQuery] DictTypePageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new DictTypeQueryPaged(request), cancellationToken)));
    }

    /// <summary>创建字典类型（dictType 唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示字典类型写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemDictAdd)]
    [OperLog("字典类型", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DictTypeInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictTypeCreateCommand(request), cancellationToken);
        return Ok(Result.Ok(affected, message: "创建成功"));
    }

    /// <summary>更新字典类型（dictType 不可变更，与原值不同则拒绝）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDictEdit)]
    [OperLog("字典类型", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] DictTypeInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictTypeUpdateCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "更新成功"));
    }

    /// <summary>删除字典类型（存在字典数据时拒绝）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDictRemove)]
    [OperLog("字典类型", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictTypeDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }

    /// <summary>恢复字典类型（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemDictRestore)]
    [OperLog("字典类型", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new DictTypeRestoreCommand(id, version), cancellationToken);
        return Ok(Result.Ok(restored, message: restored ? "恢复成功" : "字典类型不存在或未删除，无需恢复"));
    }
}
