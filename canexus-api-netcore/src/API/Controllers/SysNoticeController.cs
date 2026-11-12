
using API.Authorization;
using API.Extensions;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Notices;
using Application.Contracts.Queries;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>通知公告管理接口：分页、详情、增删改与恢复、首页最新公告（类型 / 状态取值由应用层校验）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion(ApiVersioningExtensions.CurrentVersion)]
[Route("api/system/notice")]
[Authorize]
public class SysNoticeController(ISender sender) : ControllerBase
{
    /// <summary>分页查询通知公告（title 模糊、类型 / 状态精确，按创建时间倒序；列表不含正文）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemNoticeList)]
    public async Task<IActionResult> Page([FromQuery] NoticePageRequest request, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new NoticeQueryPaged(request), cancellationToken));
    }

    /// <summary>按 Id 查询通知公告详情（含正文，供编辑回显与查看全文）。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>公告详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemNoticeQuery)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new NoticeQueryDetail(id), cancellationToken));
    }

    /// <summary>创建通知公告。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示公告写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemNoticeAdd)]
    [OperLog("通知公告", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] NoticeInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new NoticeCreateCommand(request), cancellationToken);
        return this.OkData(affected, "创建成功");
    }

    /// <summary>更新通知公告（校验规则与创建一致）。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemNoticeEdit)]
    [OperLog("通知公告", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] NoticeInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new NoticeUpdateCommand(id, version, request), cancellationToken);
        return this.OkData(affected, "更新成功");
    }

    /// <summary>删除通知公告。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemNoticeRemove)]
    [OperLog("通知公告", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new NoticeDeleteCommand(id, version), cancellationToken);
        return this.OkData(affected, "删除成功");
    }

    /// <summary>恢复通知公告（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemNoticeRestore)]
    [OperLog("通知公告", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new NoticeRestoreCommand(id, version), cancellationToken);
        return this.OkData(restored, message: restored ? "恢复成功" : "公告不存在或未删除，无需恢复");
    }

    /// <summary>查询最新通知公告（仅正常状态，按创建时间倒序；供首页公告卡片，任何登录用户可用，不校验业务权限）。</summary>
    /// <param name="count">返回条数（≤ 20，缺省 5）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>最新公告列表（不含正文）。</returns>
    [HttpGet("latest")]
    [AllowAnyAuthenticated]
    public async Task<IActionResult> Latest([FromQuery] int count = 5, CancellationToken cancellationToken = default)
    {
        return this.OkData(await sender.Send(new NoticeQueryLatest(count), cancellationToken));
    }
}
