
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using Application.Validators.Common;
using Domain.Entities;
using FluentValidation;

namespace Application.Validators;
/// <summary>字典数据请求验证器：标签样式（1~5）与状态（0/1）取值校验（消息与原服务层文案逐字一致）。</summary>
/// <typeparam name="TRequest">字典数据写入请求类型。</typeparam>
public class DictDataWriteRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : DictDataWriteRequest
{
    /// <summary>构造函数。</summary>
    public DictDataWriteRequestValidator()
    {
        RuleFor(request => request.DictType).NotBlankRule("字典类型不能为空");

        RuleFor(request => request.DictLabel).NotBlankRule("字典标签不能为空");

        RuleFor(request => request.DictValue).NotBlankRule("字典键值不能为空");

        RuleFor(request => request.TagType)
            .DefinedRule("标签样式取值不合法：仅支持 1~5（primary / success / info / warning / danger）");

        RuleFor(request => request.Status).StatusRule();
    }
}

/// <summary>创建字典数据命令验证器（委托请求验证器）；类型存在性与标签 / 键值重复由处理器校验。</summary>
public sealed class CreateDictDataCommandValidator : AbstractValidator<CreateDictDataCommand>
{
    /// <summary>构造函数。</summary>
    public CreateDictDataCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new DictDataWriteRequestValidator<DictDataCreateRequest>());
    }
}

/// <summary>更新字典数据命令验证器（委托请求验证器）；类型存在性与标签 / 键值重复由处理器校验。</summary>
public sealed class UpdateDictDataCommandValidator : AbstractValidator<UpdateDictDataCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateDictDataCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new DictDataWriteRequestValidator<DictDataUpdateRequest>());
    }
}

/// <summary>字典类型请求验证器：名称 / 类型非空（DictType 不可变更与唯一性由处理器校验）。</summary>
/// <typeparam name="TRequest">字典类型写入请求类型。</typeparam>
public class DictTypeWriteRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : DictTypeWriteRequest
{
    /// <summary>构造函数。</summary>
    public DictTypeWriteRequestValidator()
    {
        RuleFor(request => request.DictName).NotBlankRule("字典名称不能为空");
        RuleFor(request => request.DictType).NotBlankRule("字典类型不能为空");
    }
}

/// <summary>创建字典类型命令验证器（委托请求验证器）；唯一性由处理器校验。</summary>
public sealed class CreateDictTypeCommandValidator : AbstractValidator<CreateDictTypeCommand>
{
    /// <summary>构造函数。</summary>
    public CreateDictTypeCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new DictTypeWriteRequestValidator<DictTypeCreateRequest>());
    }
}

/// <summary>更新字典类型命令验证器（委托请求验证器）；DictType 不可变更由处理器校验。</summary>
public sealed class UpdateDictTypeCommandValidator : AbstractValidator<UpdateDictTypeCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateDictTypeCommandValidator()
    {
        RuleFor(command => command.Request).SetValidator(new DictTypeWriteRequestValidator<DictTypeUpdateRequest>());
    }
}
