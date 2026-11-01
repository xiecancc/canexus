
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Application.Validators.Common;
using Domain.Shared.Enums;
using FluentValidation;

namespace Application.Validators;
/// <summary>修改用户状态命令验证器：状态仅允许 1 启用 / 0 停用（错误码与原处理器文案一致）。</summary>
public sealed class UserUpdateStatusCommandValidator : AbstractValidator<UserUpdateStatusCommand>
{
    /// <summary>构造函数。</summary>
    public UserUpdateStatusCommandValidator()
    {
        RuleFor(command => command.Request.Status)
            .Must(status => status is CommonStatus.Enabled or CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");
    }
}

/// <summary>用户写入请求验证器：昵称非空、状态取值合法（承接自实体 setter 迁移的规则；创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">用户写入请求类型。</typeparam>
public class UserRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : UserRequest
{
    /// <summary>构造函数。</summary>
    public UserRequestValidator()
    {
        RuleFor(request => request.NickName).NotBlankRule("用户昵称不能为空");

        RuleFor(request => request.Status).StatusRule();
    }
}

/// <summary>创建用户命令验证器（桥接请求验证器，补充仅创建可用的用户名规则）；
/// 用户名唯一性、密码缺省与部门存在性由处理器校验。</summary>
public sealed class UserCreateCommandValidator : WriteCommandValidator<UserCreateCommand, UserCreateRequest>
{
    /// <summary>构造函数。</summary>
    public UserCreateCommandValidator() : base(new UserRequestValidator<UserCreateRequest>())
    {
        RuleFor(command => command.Request.UserName).NotBlankRule("用户名不能为空");
    }
}

/// <summary>更新用户命令验证器（桥接请求验证器）；内置 admin 编辑拒绝与角色存在性由处理器校验。</summary>
public sealed class UserUpdateCommandValidator() :
    WriteCommandValidator<UserUpdateCommand, UserUpdateRequest>(new UserRequestValidator<UserUpdateRequest>());

/// <summary>重置用户密码命令验证器：新密码须满足强度约束（乐观锁版本比对由处理器完成）。</summary>
public sealed class UserResetPasswordCommandValidator : AbstractValidator<UserResetPasswordCommand>
{
    /// <summary>构造函数。</summary>
    public UserResetPasswordCommandValidator()
    {
        RuleFor(command => command.Request.Password).PasswordRule();
    }
}

/// <summary>分配用户角色命令验证器：角色 Id 集合允许为空（清空全部角色），但不允许包含空 Guid（角色存在性由处理器校验）。</summary>
public sealed class UserUpdateRolesCommandValidator : AbstractValidator<UserUpdateRolesCommand>
{
    /// <summary>构造函数。</summary>
    public UserUpdateRolesCommandValidator()
    {
        RuleFor(command => command.Request.RoleIds)
            .Must(roleIds => roleIds.All(id => id != Guid.Empty))
            .WithMessage("角色 Id 集合包含无效的空 Guid");
    }
}
