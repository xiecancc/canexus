namespace API.Controllers;

using API.Authorization;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// 权限校验测试端点（仅集成测试使用，不出现在 OpenAPI 文档）：
/// 提供挂载 <see cref="RequirePermissionAttribute"/> 的受保护端点，验证 401 / 403 / 放行逻辑。
/// </summary>
[ApiController]
[Route("api/test-permissions")]
[ApiExplorerSettings(IgnoreApi = true)]
public class TestPermissionController : ControllerBase
{
    /// <summary>需要 test:protected:list 权限的端点。</summary>
    /// <returns>固定成功响应。</returns>
    [HttpGet("protected")]
    [Authorize]
    [RequirePermission("test:protected:list")]
    public IActionResult Protected()
    {
        return Ok(Result.Ok("ok"));
    }
}
