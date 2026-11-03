
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>字典数据请求验证器：标签样式（1~5）与状态（0/1）取值校验（消息与原服务层文案逐字一致）。</summary>
/// <typeparam name="TRequest">字典数据写入请求类型。</typeparam>
public class DictDataRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : DictDataInputRequest
{
    /// <summary>构造函数。</summary>
    public DictDataRequestValidator()
    {
        RuleFor(request => request.DictType).NotBlankRule("字典类型不能为空");

        RuleFor(request => request.DictLabel).NotBlankRule("字典标签不能为空");

        RuleFor(request => request.DictValue).NotBlankRule("字典键值不能为空");

        RuleFor(request => request.TagType)
            .DefinedRule("标签样式取值不合法：仅支持 1~5（primary / success / info / warning / danger）");

        RuleFor(request => request.Status).StatusRule();
    }
}

/// <summary>创建字典数据命令验证器（桥接请求验证器）；类型存在性与标签 / 键值重复由处理器校验。</summary>
public sealed class DictDataCreateCommandValidator() :
    WriteCommandValidator<DictDataCreateCommand, DictDataInputRequest>(new DictDataRequestValidator<DictDataInputRequest>());

/// <summary>更新字典数据命令验证器（桥接请求验证器）；类型存在性与标签 / 键值重复由处理器校验。</summary>
public sealed class DictDataUpdateCommandValidator() :
    UpdateCommandValidator<DictDataUpdateCommand, DictDataInputRequest>(new DictDataRequestValidator<DictDataInputRequest>());

/// <summary>字典类型请求验证器：名称 / 类型非空（DictType 不可变更与唯一性由处理器校验）。</summary>
/// <typeparam name="TRequest">字典类型写入请求类型。</typeparam>
public class DictTypeRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : DictTypeInputRequest
{
    /// <summary>构造函数。</summary>
    public DictTypeRequestValidator()
    {
        RuleFor(request => request.DictName).NotBlankRule("字典名称不能为空");
        RuleFor(request => request.DictType).NotBlankRule("字典类型不能为空");
    }
}

/// <summary>创建字典类型命令验证器（桥接请求验证器）；唯一性由处理器校验。</summary>
public sealed class DictTypeCreateCommandValidator() :
    WriteCommandValidator<DictTypeCreateCommand, DictTypeInputRequest>(new DictTypeRequestValidator<DictTypeInputRequest>());

/// <summary>更新字典类型命令验证器（桥接请求验证器）；DictType 不可变更由处理器校验。</summary>
public sealed class DictTypeUpdateCommandValidator() :
    UpdateCommandValidator<DictTypeUpdateCommand, DictTypeInputRequest>(new DictTypeRequestValidator<DictTypeInputRequest>());

/// <summary>删除字典类型命令验证器（乐观锁版本号入口兜底）；DictType 内置保护由处理器校验。</summary>
public sealed class DictTypeDeleteCommandValidator() : DeleteCommandValidator<DictTypeDeleteCommand>;
