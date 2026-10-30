
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Contracts.Queries;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>参数配置管理接口：分页、按键名取值、增删改（configKey 唯一；内置参数不可删除）。</summary>
[ApiController]
[Route("api/system/config")]
[Authorize]
public class SysConfigController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>构造函数。</summary>
    /// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
    public SysConfigController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>分页查询参数配置（keyword 模糊匹配名称 / 键名 / 键值）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:config:list")]
    public async Task<IActionResult> Page([FromQuery] ConfigPageRequest request)
    {
        return Ok(Result.Ok(await _sender.Send(new GetConfigPageQuery(request))));
    }

    /// <summary>按参数键名查询参数值（走缓存，写操作后立即刷新）。</summary>
    /// <param name="configKey">参数键名。</param>
    /// <returns>参数值字符串。</returns>
    [HttpGet("key/{configKey}")]
    [RequirePermission("system:config:query")]
    public async Task<IActionResult> GetByKey(string configKey)
    {
        // 显式泛型参数：避免 string 实参被解析为非泛型 Ok(message) 重载导致 data 丢失
        return Ok(Result.Ok<string>(await _sender.Send(new GetConfigByKeyQuery(configKey))));
    }

    /// <summary>创建参数配置（configKey 唯一，创建项一律非内置）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新参数 Id。</returns>
    [HttpPost]
    [RequirePermission("system:config:add")]
    [OperLog("参数配置", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] ConfigCreateRequest request)
    {
        var id = await _sender.Send(new CreateConfigCommand(request));
        return Ok(Result.Ok(id, "创建成功"));
    }

    /// <summary>更新参数配置（键名可变更，新旧键缓存均失效；内置参数可编辑）。</summary>
    /// <param name="id">参数 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("system:config:edit")]
    [OperLog("参数配置", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConfigUpdateRequest request)
    {
        await _sender.Send(new UpdateConfigCommand(id, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除参数配置（内置参数拒绝删除）。</summary>
    /// <param name="id">参数 Id。</param>
    /// <returns>统一响应。</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("system:config:remove")]
    [OperLog("参数配置", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteConfigCommand(id));
        return Ok(Result.Ok(message: "删除成功"));
    }
}
