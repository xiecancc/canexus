
using API.Authorization;
using Asp.Versioning;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests;
/// <summary>
/// 权限链路测试专用受保护端点：仅存在于测试程序集，由 <see cref="ApiTestFactory"/> 经 ApplicationPart 注册。
/// <para>生产 API 程序集不包含该路由，也不包含该权限标识；测试权限不进入 PermissionDefinitions 注册表。</para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/test-permissions")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize]
public class TestPermissionController : ControllerBase
{
    /// <summary>测试专用的受保护权限标识（仅测试程序集内使用）。</summary>
    public const string Permission = "test:protected:list";

    /// <summary>需要测试权限的端点。</summary>
    /// <returns>固定成功响应。</returns>
    [HttpGet("protected")]
    [RequirePermission(Permission)]
    public IActionResult Protected() => Ok(Result.OkMessage("ok"));
}
