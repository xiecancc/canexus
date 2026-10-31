
using Application.Contracts.Commands;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>通知公告命令验证器：标题（非空白且 ≤200）、类型（1 通知 / 2 公告）、状态（1 正常 / 0 关闭）、内容非空白。
/// 错误码保持 NoticeRuleInvalid（401002）与原服务层文案一致；创建 / 更新共用同一校验方法消除重复。</summary>
public static class NoticeValidation
{
    /// <summary>公告字段校验（创建 / 更新共用）。</summary>
    /// <param name="title">公告标题。</param>
    /// <param name="noticeType">公告类型。</param>
    /// <param name="status">公告状态。</param>
    /// <param name="content">公告内容。</param>
    public static void ValidateNoticeFields(string title, NoticeType noticeType, CommonStatus status, string content)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new BusinessException(ErrorCodes.NoticeRuleInvalid, "公告标题不合法：不能为空且不超过 200 个字符");
        }

        if (noticeType is not (NoticeType.Notification or NoticeType.Announcement))
        {
            throw new BusinessException(ErrorCodes.NoticeRuleInvalid, "公告类型取值不合法：仅支持 1 通知 / 2 公告");
        }

        if (status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.NoticeRuleInvalid, "公告状态取值不合法：仅支持 1 正常 / 0 关闭");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BusinessException(ErrorCodes.NoticeRuleInvalid, "公告内容不能为空");
        }
    }
}

/// <summary>创建通知公告命令验证器（Custom 规则抛模块错误码）。</summary>
public sealed class CreateNoticeCommandValidator : AbstractValidator<CreateNoticeCommand>
{
    /// <summary>构造函数。</summary>
    public CreateNoticeCommandValidator()
    {
        RuleFor(command => command.Request)
            .Custom((request, context) => NoticeValidation.ValidateNoticeFields(
                request.Title, request.NoticeType, request.Status, request.Content));
    }
}

/// <summary>更新通知公告命令验证器（Custom 规则抛模块错误码）。</summary>
public sealed class UpdateNoticeCommandValidator : AbstractValidator<UpdateNoticeCommand>
{
    /// <summary>构造函数。</summary>
    public UpdateNoticeCommandValidator()
    {
        RuleFor(command => command.Request)
            .Custom((request, context) => NoticeValidation.ValidateNoticeFields(
                request.Title, request.NoticeType, request.Status, request.Content));
    }
}
