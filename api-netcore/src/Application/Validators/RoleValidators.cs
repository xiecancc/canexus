
using Application.Contracts.Commands;
using Application.Contracts.Roles;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>角色命令验证器：名称 / 权限字符非空、数据范围（1~5）、状态（启用/停用）；
/// 错误码保持 CommonInvalidRequest（40000）与原服务层文案一致；roleKey 唯一性与保留值校验由处理器完成；
/// 创建 / 更新共用同一校验方法消除重复。</summary>
public static class RoleValidation
{
    /// <summary>角色字段校验（创建 / 更新共用）。</summary>
    /// <param name="roleName">角色名称。</param>
    /// <param name="roleKey">角色权限字符。</param>
    /// <param name="dataScope">数据范围。</param>
    /// <param name="status">状态。</param>
    public static void ValidateRoleFields(string roleName, string roleKey, DataScope dataScope, CommonStatus status)
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

/// <summary>创建角色命令验证器（Custom 规则抛模块错误码）。</summary>
public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    /// <summary>构造函数。</summary>
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Request)
            .Custom((request, context) => RoleValidation.ValidateRoleFields(
                request.RoleName, request.RoleKey, request.DataScope, request.Status));
    }
}

/// <summary>更新角色命令验证器（Custom 规则抛模块错误码）。</summary>
public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateRoleCommandValidator()
    {
        RuleFor(command => command.Request)
            .Custom((request, context) => RoleValidation.ValidateRoleFields(
                request.RoleName, request.RoleKey, request.DataScope, request.Status));
    }
}
