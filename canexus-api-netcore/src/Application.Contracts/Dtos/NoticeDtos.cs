using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Notices;
/// <summary>通知公告分页查询请求（GET /api/system/notice/page）。</summary>
public class NoticePageRequest : AggregatePagedRequest
{
    /// <summary>公告标题（模糊匹配）。</summary>
    public string? Title { get; set; }

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType? NoticeType { get; set; }

    /// <summary>状态：正常（1）/ 关闭（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>通知公告列表行（分页数据项）：直接继承聚合分页最小根（正文仅详情接口填充，列表返回 null 以减小载荷）。</summary>
public class NoticePagedResponse : AggregatePagedResponse
{
    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }

    /// <summary>状态：正常（1），关闭（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>公告内容（仅详情接口返回，列表恒为 null）。</summary>
    public string? Content { get; set; }
}

/// <summary>通知公告详情（GET /api/system/notice/{id}）：直接继承聚合详情根，业务字段平铺。</summary>
public class NoticeDetailResponse : AggregateDetailResponse
{
    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }

    /// <summary>状态：正常（1），关闭（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>公告内容。</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>通知公告写入请求（POST / PUT 共用载荷；镜像 SysNotice 聚合的启停 / 备注与业务字段）。</summary>
public class NoticeInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>公告标题（必填，不超过 200 字符）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }

    /// <summary>公告内容（必填）。</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>最新通知公告简项（GET /api/system/notice/latest 返回项，不含正文，供首页公告卡片展示）。</summary>
public class NoticeLatestResponse : AggregatePagedResponse
{
    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }
}
