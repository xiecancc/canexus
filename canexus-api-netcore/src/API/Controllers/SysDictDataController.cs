
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
/// <summary>字典数据管理接口：分页、按类型下拉、增删改与恢复（同类型下标签 / 键值唯一）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/dict-data")]
[Authorize]
public class SysDictDataController(ISender sender) : ControllerBase
{
    /// <summary>分页查询字典数据（dictType 精确、keyword 模糊匹配标签 / 键值）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemDictList)]
    public async Task<IActionResult> Page([FromQuery] DictDataPageRequest request, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new DictDataQueryPaged(request), cancellationToken));
    }

    /// <summary>按字典类型查询启用状态的字典数据（走缓存，写操作后立即刷新）。</summary>
    /// <param name="dictType">字典类型。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>字典数据简项列表。</returns>
    [HttpGet("type/{dictType}")]
    [RequirePermission(PermissionDefinitions.SystemDictQuery)]
    public async Task<IActionResult> GetByType(string dictType, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new DictDataQueryByType(dictType), cancellationToken));
    }

    /// <summary>创建字典数据（所属类型必须存在；同类型下标签 / 键值唯一）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示字典数据写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemDictAdd)]
    [OperLog("字典数据", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] DictDataInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictDataCreateCommand(request), cancellationToken);
        return this.OkData(affected, "创建成功");
    }

    /// <summary>更新字典数据（类型可迁移，迁移后新旧类型缓存均失效）。</summary>
    /// <param name="id">字典数据 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDictEdit)]
    [OperLog("字典数据", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] DictDataInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictDataUpdateCommand(id, version, request), cancellationToken);
        return this.OkData(affected, "更新成功");
    }

    /// <summary>删除字典数据。</summary>
    /// <param name="id">字典数据 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemDictRemove)]
    [OperLog("字典数据", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new DictDataDeleteCommand(id, version), cancellationToken);
        return this.OkData(affected, "删除成功");
    }

    /// <summary>恢复字典数据（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">字典数据 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemDictRestore)]
    [OperLog("字典数据", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new DictDataRestoreCommand(id, version), cancellationToken);
        return this.OkData(restored, message: restored ? "恢复成功" : "字典数据不存在或未删除，无需恢复");
    }
}
