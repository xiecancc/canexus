
using API.Authorization;
using API.Logging;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Application.Contracts.Users;
using Asp.Versioning;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
/// <summary>用户管理接口：分页、详情、增删改与恢复、状态维护、重置密码与角色分配。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="sender">MediatR 请求发送器（命令 / 查询请求分发入口）。</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/system/user")]
[Authorize]
public class SysUserController(ISender sender) : ControllerBase
{
    /// <summary>分页查询用户（keyword 匹配用户名 / 昵称 / 手机号；deptId 按部门及以下；叠加数据范围过滤）。</summary>
    /// <param name="request">分页与筛选条件。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>分页结果。</returns>
    [HttpGet("page")]
    [RequirePermission(PermissionDefinitions.SystemUserList)]
    public async Task<IActionResult> Page([FromQuery] UserPageRequest request, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new UserQueryPaged(request), cancellationToken)));
    }

    /// <summary>获取用户详情（含角色 Id 集合）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>用户详情。</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionDefinitions.SystemUserQuery)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        return Ok(Result.Ok(await sender.Send(new UserQueryDetail(id), cancellationToken)));
    }

    /// <summary>创建用户（用户名唯一；密码缺省取参数配置 sys.user.initPassword，一律首登强制改密）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>创建结果（data 为 bool，表示用户写命令是否生效；不返回新 Id）。</returns>
    [HttpPost]
    [RequirePermission(PermissionDefinitions.SystemUserAdd)]
    [OperLog("用户管理", OperBusinessType.Add)]
    public async Task<IActionResult> Create([FromBody] UserInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserCreateCommand(request), cancellationToken);
        return Ok(Result.Ok(affected, message: "创建成功"));
    }

    /// <summary>批量导入用户（Excel/xlsx，multipart/form-data；全量成功才入库，行级错误随响应回执返回）。
    /// 未选择文件时抛业务异常（UserImportFileInvalid），由全局异常中间件统一映射 HTTP 状态（P1-R1）。</summary>
    /// <param name="file">导入文件（列名：用户名 / 昵称 / 邮箱 / 手机号 / 部门Id / 初始密码）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>导入回执（总数 / 成功 / 失败 / 行级错误明细）。</returns>
    [HttpPost("import")]
    [RequirePermission(PermissionDefinitions.SystemUserImport)]
    [OperLog("用户管理", OperBusinessType.Import)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessException(ErrorCodes.UserImportFileInvalid, "请选择要导入的 Excel 文件");
        }

        await using var stream = file.OpenReadStream();
        var data = await sender.Send(new UserImportCommand(stream), cancellationToken);
        return Ok(Result.Ok(data, message: data.FailureCount == 0 ? $"导入成功，共 {data.SuccessCount} 条" : "导入失败，详见行级错误明细（未写入任何数据）"));
    }

    /// <summary>更新用户（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemUserEdit)]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> Update(Guid id, Guid version, [FromBody] UserInputRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserUpdateCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "更新成功"));
    }

    /// <summary>删除用户（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    /// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
    [HttpDelete("{id:guid}/{version:guid}")]
    [RequirePermission(PermissionDefinitions.SystemUserRemove)]
    [OperLog("用户管理", OperBusinessType.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserDeleteCommand(id, version), cancellationToken);
        return Ok(Result.Ok(affected, message: "删除成功"));
    }

    /// <summary>恢复用户（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="version">乐观锁版本号（与删除相同的路由参数模式）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPatch("{id:guid}/{version:guid}/restore")]
    [RequirePermission(PermissionDefinitions.SystemUserRestore)]
    [OperLog("用户管理", OperBusinessType.Restore)]
    public async Task<IActionResult> Restore(Guid id, Guid version, CancellationToken cancellationToken = default)
    {
        var restored = await sender.Send(new UserRestoreCommand(id, version), cancellationToken);
        return Ok(Result.Ok(restored, message: restored ? "恢复成功" : "用户不存在或未删除，无需恢复"));
    }

    /// <summary>修改用户状态（内置超管 admin 拒绝操作）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">状态请求。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/status")]
    [RequirePermission(PermissionDefinitions.SystemUserEdit)]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> UpdateStatus(Guid id, Guid version, [FromBody] UserStatusRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserUpdateStatusCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "状态更新成功"));
    }

    /// <summary>重置密码（BCrypt 存储）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">新密码。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/password")]
    [RequirePermission(PermissionDefinitions.SystemUserResetPwd)]
    [OperLog("用户管理", OperBusinessType.Update)]
    public async Task<IActionResult> ResetPassword(Guid id, Guid version, [FromBody] UserPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserResetPasswordCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "密码重置成功"));
    }

    /// <summary>分配角色（全量替换）。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="request">角色 Id 集合。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>统一响应。</returns>
    [HttpPut("{id:guid}/{version:guid}/roles")]
    [RequirePermission(PermissionDefinitions.SystemUserEdit)]
    [OperLog("用户管理", OperBusinessType.Grant)]
    public async Task<IActionResult> UpdateRoles(Guid id, Guid version, [FromBody] UserRolesRequest request, CancellationToken cancellationToken = default)
    {
        var affected = await sender.Send(new UserUpdateRolesCommand(id, version, request), cancellationToken);
        return Ok(Result.Ok(affected, message: "角色分配成功"));
    }

    /// <summary>导出用户列表（xlsx；复用列表同口径筛选与数据范围过滤，行数上限 10000）。</summary>
    /// <param name="request">用户筛选条件（分页参数被忽略，全量按筛选拉取）。</param>
    /// <param name="cancellationToken">请求取消令牌（客户端断开即中止该请求，由 Send 透传至 MediatR 管道）。</param>
    /// <returns>Excel 文件流。</returns>
    [HttpGet("export")]
    [RequirePermission(PermissionDefinitions.SystemUserExport)]
    [OperLog("用户管理", OperBusinessType.Export)]
    public async Task<IActionResult> Export([FromQuery] UserPageRequest request, CancellationToken cancellationToken = default)
    {
        var file = await sender.Send(new UserExportQuery(request), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

}
