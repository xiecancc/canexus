
using Application.Contracts.Commands;
using Application.Contracts.Roles;
using Application.Validators.Common;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>角色写入请求验证器：名称 / 权限字符非空、数据范围（1~5）、状态（启用/停用）；
/// 错误码保持 CommonInvalidRequest（40000）与原服务层文案一致；创建 / 更新共用（消除重复定义）。</summary>
/// <typeparam name="TRequest">角色写入请求类型。</typeparam>
public class RoleRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : RoleInputRequest
{
    /// <summary>构造函数。</summary>
    public RoleRequestValidator()
    {
        RuleFor(request => request)
            .Custom((request, context) => ValidateRoleFields(
                request.RoleName, request.RoleKey, request.DataScope, request.Status));
    }

    /// <summary>角色字段校验（错误码与文案逐字保持原服务层语义）。</summary>
    private static void ValidateRoleFields(string roleName, string roleKey, DataScope dataScope, CommonStatus status)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "角色名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(roleKey))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "角色权限字符不能为空");
        }

        if (!Enum.IsDefined(dataScope))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "数据范围取值不合法（1~5）");
        }

        if (status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "状态取值不合法：仅支持 1 启用 / 0 停用");
        }
    }
}

/// <summary>创建角色命令验证器（桥接请求验证器）；roleKey 唯一性与保留值校验由处理器完成。</summary>
public sealed class RoleCreateCommandValidator() :
    WriteCommandValidator<RoleCreateCommand, RoleInputRequest>(new RoleRequestValidator<RoleInputRequest>());

/// <summary>更新角色命令验证器（桥接请求验证器）；roleKey 唯一性与保留值校验由处理器完成。</summary>
public sealed class RoleUpdateCommandValidator() :
    UpdateCommandValidator<RoleUpdateCommand, RoleInputRequest>(new RoleRequestValidator<RoleInputRequest>());

/// <summary>删除角色命令验证器（乐观锁版本号入口兜底）。</summary>
public sealed class RoleDeleteCommandValidator() : DeleteCommandValidator<RoleDeleteCommand>;

/// <summary>修改角色状态命令验证器：状态仅允许 1 启用 / 0 停用（super_admin 保护由处理器完成）。</summary>
public sealed class RoleUpdateStatusCommandValidator : AbstractValidator<RoleUpdateStatusCommand>
{
    /// <summary>构造函数。</summary>
    public RoleUpdateStatusCommandValidator()
    {
        RuleFor(command => command.Request.Status)
            .Must(status => status is CommonStatus.Enabled or CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");

        RuleFor(command => command.Version).NoEmptyRule("乐观锁版本号不能为空，请回传详情 / 列表接口返回的 Version");
    }
}
