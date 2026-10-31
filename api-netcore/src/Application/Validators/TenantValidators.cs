
using Application.Contracts.Commands;
using FluentValidation;

namespace Application.Validators;
/// <summary>创建租户命令验证器：标识 / 名称非空且长度受列宽约束，状态取值合法（消息与原服务层文案逐字一致）。</summary>
public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    /// <summary>构造函数。</summary>
    public CreateTenantCommandValidator()
    {
        RuleFor(command => command.Request.TenantKey)
            .Must(key => !string.IsNullOrEmpty(key?.Trim()))
            .WithMessage("租户标识不能为空")
            .Must(key => key!.Trim().Length <= 50)
            .WithMessage("租户标识长度不能超过 50 个字符");

        RuleFor(command => command.Request.TenantName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("租户名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("租户名称长度不能超过 50 个字符");

        RuleFor(command => command.Request.Status)
            .Must(status => status is Domain.Shared.Enums.CommonStatus.Enabled or Domain.Shared.Enums.CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");
    }
}

/// <summary>更新租户命令验证器：标识 / 名称非空且长度受列宽约束，状态取值合法（消息与原服务层文案逐字一致）。</summary>
public sealed class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateTenantCommandValidator()
    {
        RuleFor(command => command.Request.TenantKey)
            .Must(key => !string.IsNullOrEmpty(key?.Trim()))
            .WithMessage("租户标识不能为空")
            .Must(key => key!.Trim().Length <= 50)
            .WithMessage("租户标识长度不能超过 50 个字符");

        RuleFor(command => command.Request.TenantName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("租户名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("租户名称长度不能超过 50 个字符");

        RuleFor(command => command.Request.Status)
            .Must(status => status is Domain.Shared.Enums.CommonStatus.Enabled or Domain.Shared.Enums.CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");
    }
}
