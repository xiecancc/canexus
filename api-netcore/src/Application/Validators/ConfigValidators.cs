
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Validators.Common;
using Domain.Entities;
using FluentValidation;

namespace Application.Validators;
/// <summary>参数配置请求验证器：键名格式 / 名称非空（创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">参数配置写入请求类型。</typeparam>
public class ConfigWriteRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : ConfigWriteRequest
{
    /// <summary>构造函数。</summary>
    public ConfigWriteRequestValidator()
    {
        RuleFor(request => request.ConfigKey).ConfigKeyRule();
        RuleFor(request => request.ConfigName).NotBlankRule("参数名称不能为空");
    }
}

/// <summary>创建参数配置命令验证器（委托请求验证器）；业务重复由处理器校验。</summary>
public sealed class CreateConfigCommandValidator : AbstractValidator<CreateConfigCommand>
{
    /// <summary>构造函数。</summary>
    public CreateConfigCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new ConfigWriteRequestValidator<ConfigCreateRequest>());
    }
}

/// <summary>更新参数配置命令验证器（委托请求验证器）；业务重复由处理器校验。</summary>
public sealed class UpdateConfigCommandValidator : AbstractValidator<UpdateConfigCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateConfigCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new ConfigWriteRequestValidator<ConfigUpdateRequest>());
    }
}
