
using Application.Contracts.Commands;
using Application.Contracts.Depts;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>部门写入请求验证器：名称非空、状态取值合法（承接自实体 setter 迁移的规则；创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">部门写入请求类型。</typeparam>
public class DeptRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : DeptRequest
{
    /// <summary>构造函数。</summary>
    public DeptRequestValidator()
    {
        RuleFor(request => request.DeptName).NotBlankRule("部门名称不能为空");

        RuleFor(request => request.Status).StatusRule();
    }
}

/// <summary>创建部门命令验证器（桥接请求验证器）；父部门存在性与祖级路径由处理器 / 树形仓储校验。</summary>
public sealed class DeptCreateCommandValidator() :
    WriteCommandValidator<DeptCreateCommand, DeptCreateRequest>(new DeptRequestValidator<DeptCreateRequest>());

/// <summary>更新部门命令验证器（桥接请求验证器）；父级不可选自身或后代由处理器 / 树形仓储校验。</summary>
public sealed class DeptUpdateCommandValidator() :
    WriteCommandValidator<DeptUpdateCommand, DeptUpdateRequest>(new DeptRequestValidator<DeptUpdateRequest>());
