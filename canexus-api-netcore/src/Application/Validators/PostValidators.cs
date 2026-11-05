
using Application.Contracts.Commands;
using Application.Contracts.Posts;
using Application.Validators.Common;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>岗位写入请求验证器：名称（非空白且 ≤50）、编码（非空白、≤64、字母数字下划线连字符）、状态（1 启用 / 0 停用）、顺序（0 ~ 9999）。
/// 错误码 PostRuleInvalid（401303）；创建 / 更新共用（消除重复定义）。</summary>
public class PostRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : PostInputRequest
{
    /// <summary>构造函数。</summary>
    public PostRequestValidator()
    {
        RuleFor(request => request)
            .Custom((request, context) => ValidatePostFields(
                request.PostName, request.PostCode, request.Status, request.OrderNum));
    }

    /// <summary>岗位字段校验。</summary>
    private static void ValidatePostFields(string postName, string postCode, CommonStatus status, int orderNum)
    {
        if (string.IsNullOrWhiteSpace(postName) || postName.Trim().Length > 50)
        {
            throw new BusinessException(ErrorCodes.PostRuleInvalid, "岗位名称不合法：不能为空且不超过 50 个字符");
        }

        if (string.IsNullOrWhiteSpace(postCode)
            || postCode.Trim().Length > 64
            || !System.Text.RegularExpressions.Regex.IsMatch(postCode.Trim(), "^[a-zA-Z0-9_-]+$"))
        {
            throw new BusinessException(ErrorCodes.PostRuleInvalid, "岗位编码不合法：不能为空、不超过 64 个字符且仅含字母 / 数字 / 下划线 / 连字符");
        }

        if (status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.PostRuleInvalid, "岗位状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        if (orderNum is < 0 or > 9999)
        {
            throw new BusinessException(ErrorCodes.PostRuleInvalid, "显示顺序取值不合法：仅支持 0 ~ 9999");
        }
    }
}

/// <summary>创建岗位命令验证器（桥接请求验证器）。</summary>
public sealed class PostCreateCommandValidator() :
    WriteCommandValidator<PostCreateCommand, PostInputRequest>(new PostRequestValidator<PostInputRequest>());

/// <summary>更新岗位命令验证器（桥接请求验证器）。</summary>
public sealed class PostUpdateCommandValidator() :
    UpdateCommandValidator<PostUpdateCommand, PostInputRequest>(new PostRequestValidator<PostInputRequest>());

/// <summary>删除岗位命令验证器（乐观锁版本号入口兜底）。</summary>
public sealed class PostDeleteCommandValidator() : DeleteCommandValidator<PostDeleteCommand>;
