
using Asp.Versioning;
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Users;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>用户管理接口：分页、详情、增删改与恢复、状态维护、重置密码与角色分配。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（CQRS 分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/user")]
[Authorize]
public class SysUserController(ISender sender) : ControllerBase
{
    /// <summary>分页查询用户（keyword 匹配用户名 / 昵称 / 手机号；deptId 按部门及以下；叠加数据范围过滤）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission("system:user:list")]
    public async Task<IActionResult> Page([FromQuery] UserPageRequest request)
    {
        return Ok(Result.Ok(await sender.Send(new UserQueryPaged(request))));
    }

    /// <summary>获取用户详情（含角色 Id 集合）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <returns>用户详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("system:user:query")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(Result.Ok(await sender.Send(new UserQueryDetail(id))));
    }

    /// <summary>创建用户（用户名唯一；密码缺省 "123456"）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>新用户 Id。</returns>
    [HttpPost]
    [RequirePermission("system:user:add")]
    [OperLog("用户管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] UserInputRequest request)
    {
        await sender.Send(new UserCreateCommand(request));
        return Ok(Result.Ok(message: "创建成功"));
    }

    /// <summary>批量导入用户（Excel/xlsx，multipart/form-data；全量成功才入库，行级错误随响应回执返回）。</summary>
    /// <param name="file">导入文件（列名：用户名 / 昵称 / 邮箱 / 手机号 / 部门Id / 初始密码）。</param>
    /// <returns>导入回执（总数 / 成功 / 失败 / 行级错误明细）。</returns>
    [HttpPost("import")]
    [RequirePermission("system:user:import")]
    [OperLog("用户管理", OperBusinessType.Import)]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return Ok(Result.Fail(ErrorCodes.UserImportFileInvalid, "请选择要导入的 Excel 文件"));
        }

        await using var stream = file.OpenReadStream();
        var data = await sender.Send(new UserImportCommand(stream));
        return Ok(Result.Ok(data, message: data.FailureCount == 0 ? $"导入成功，共 {data.SuccessCount} 条" : "导入失败，详见行级错误明细（未写入任何数据）"));
    }

    /// <summary>更新用户（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission("system:user:edit")]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] UserInputRequest request)
    {
        await sender.Send(new UserUpdateCommand(id, version, request));
        return Ok(Result.Ok(message: "更新成功"));
    }

    /// <summary>删除用户（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission("system:user:remove")]
    [OperLog("用户管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version)
    {
        await sender.Send(new UserDeleteCommand(id, version));
        return Ok(Result.Ok(message: "删除成功"));
    }

    /// <summary>恢复用户（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission("system:user:restore")]
    [OperLog("用户管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version)
    {
        var restored = await sender.Send(new UserRestoreCommand(id, version));
        return Ok(Result.Ok(message: restored ? "恢复成功" : "用户不存在或未删除，无需恢复"));
    }

    /// <summary>修改用户状态（内置超管 admin 拒绝操作）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">状态请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/status")]
    [RequirePermission("system:user:edit")]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> UpdateStatus(Guid id, Guid version, [FromBody] UserStatusRequest request)
    {
        await sender.Send(new UserUpdateStatusCommand(id, version, request));
        return Ok(Result.Ok(message: "状态更新成功"));
    }

    /// <summary>重置密码（BCrypt 存储）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">新密码。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/password")]
    [RequirePermission("system:user:resetPwd")]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> ResetPassword(Guid id, Guid version, [FromBody] UserPasswordRequest request)
    {
        await sender.Send(new UserResetPasswordCommand(id, version, request));
        return Ok(Result.Ok(message: "密码重置成功"));
    }

    /// <summary>分配角色（全量替换）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">角色 Id 集合。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/roles")]
    [RequirePermission("system:user:edit")]
    [OperLog("用户管理", OperBusinessType.Grant)]
    public async Task<IActionResult> UpdateRoles(Guid id, Guid version, [FromBody] UserRolesRequest request)
    {
        await sender.Send(new UserUpdateRolesCommand(id, version, request));
        return Ok(Result.Ok(message: "角色分配成功"));
    }
}
