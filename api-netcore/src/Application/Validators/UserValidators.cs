
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Application.Validators.Common;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>修改用户状态命令验证器：状态仅允许 1 启用 / 0 停用（错误码与原处理器文案一致）。</summary>
public sealed class UpdateUserStatusCommandValidator : AbstractValidator<UpdateUserStatusCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateUserStatusCommandValidator()
    {
        RuleFor(command => command.Request.Status)
            .Must(status => status is CommonStatus.Enabled or CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");
    }
}

/// <summary>用户写入请求验证器：昵称非空、状态取值合法（承接自实体 setter 迁移的规则；创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">用户写入请求类型。</typeparam>
public class UserWriteRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : UserWriteRequest
{
    /// <summary>构造函数。</summary>
    public UserWriteRequestValidator()
    {
        RuleFor(request => request.NickName).NotBlankRule("用户昵称不能为空");

        RuleFor(request => request.Status).StatusRule();
    }
}

/// <summary>创建用户命令验证器：用户名非空（唯一性与部门存在性由处理器校验）。</summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>构造函数。</summary>
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Request.UserName).NotBlankRule("用户名不能为空");

        RuleFor(command => command.Request).SetValidator(new UserWriteRequestValidator<UserCreateRequest>());
    }
}

/// <summary>更新用户命令验证器：昵称非空、状态取值合法（内置 admin 编辑拒绝与角色存在性由处理器校验）。</summary>
public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new UserWriteRequestValidator<UserUpdateRequest>());
    }
}
