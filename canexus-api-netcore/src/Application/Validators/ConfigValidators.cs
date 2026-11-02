
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>参数配置请求验证器：键名格式 / 名称非空（创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">参数配置写入请求类型。</typeparam>
public class ConfigRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : ConfigInputRequest
{
    /// <summary>构造函数。</summary>
    public ConfigRequestValidator()
    {
        RuleFor(request => request.ConfigKey).ConfigKeyRule();
        RuleFor(request => request.ConfigName).NotBlankRule("参数名称不能为空");
    }
}

/// <summary>创建参数配置命令验证器（桥接请求验证器）；业务重复由处理器校验。</summary>
public sealed class ConfigCreateCommandValidator() :
    WriteCommandValidator<ConfigCreateCommand, ConfigInputRequest>(new ConfigRequestValidator<ConfigInputRequest>());

/// <summary>更新参数配置命令验证器（桥接请求验证器）；业务重复由处理器校验。</summary>
public sealed class ConfigUpdateCommandValidator() :
    UpdateCommandValidator<ConfigUpdateCommand, ConfigInputRequest>(new ConfigRequestValidator<ConfigInputRequest>());

/// <summary>删除参数配置命令验证器（乐观锁版本号入口兜底）。</summary>
public sealed class ConfigDeleteCommandValidator() : DeleteCommandValidator<ConfigDeleteCommand>;
