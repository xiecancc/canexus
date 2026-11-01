
using Application.Contracts.Commands;
using Application.Contracts.Tenants;
using Application.Validators.Common;
using Domain.Shared.Enums;
using FluentValidation;

namespace Application.Validators;
/// <summary>租户写入请求验证器：标识 / 名称非空且长度受列宽约束，状态取值合法
/// （消息与原服务层文案逐字一致；创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">租户写入请求类型。</typeparam>
public class TenantRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : TenantRequest
{
    /// <summary>构造函数。</summary>
    public TenantRequestValidator()
    {
        RuleFor(request => request.TenantKey)
            .Must(key => !string.IsNullOrEmpty(key?.Trim()))
            .WithMessage("租户标识不能为空")
            .Must(key => key!.Trim().Length <= 50)
            .WithMessage("租户标识长度不能超过 50 个字符");

        RuleFor(request => request.TenantName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("租户名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("租户名称长度不能超过 50 个字符");

        RuleFor(request => request.Status)
            .Must(status => status is CommonStatus.Enabled or CommonStatus.Disabled)
            .WithMessage("状态取值不合法：仅支持 1 启用 / 0 停用");
    }
}

/// <summary>创建租户命令验证器（桥接请求验证器）；tenantKey 唯一性由处理器校验。</summary>
public sealed class TenantCreateCommandValidator() :
    WriteCommandValidator<TenantCreateCommand, TenantCreateRequest>(new TenantRequestValidator<TenantCreateRequest>());

/// <summary>更新租户命令验证器（桥接请求验证器）；tenantKey 不可变更由处理器校验。</summary>
public sealed class TenantUpdateCommandValidator() :
    WriteCommandValidator<TenantUpdateCommand, TenantUpdateRequest>(new TenantRequestValidator<TenantUpdateRequest>());
