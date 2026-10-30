namespace Application.Contracts.Notices;

using Application.Contracts.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

/// <summary>通知公告分页查询请求（GET /api/system/notice/page）。</summary>
public class NoticePageRequest : PagedRequest
{
    /// <summary>公告标题（模糊匹配）。</summary>
    public string? Title { get; set; }

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType? NoticeType { get; set; }

    /// <summary>状态：正常（1）/ 关闭（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>通知公告 VO 公共基类：对齐 SysNotice 聚合的业务字段。</summary>
public abstract class NoticeBaseVO
{
    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }

    /// <summary>状态：正常（1），关闭（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>通知公告列表行（分页数据项；正文字段仅详情接口填充，列表返回 null 以减小载荷）。</summary>
public class NoticeListVO : NoticeBaseVO
{
    /// <summary>公告 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>公告内容（仅详情接口返回，列表恒为 null）。</summary>
    public string? Content { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>通知公告写入请求基类：创建 / 更新共同字段（对齐 SysNotice 聚合）。</summary>
public abstract class NoticeWriteRequest : StatusDescriptionRequest
{
    /// <summary>公告标题（必填，不超过 200 字符）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }

    /// <summary>公告内容（必填）。</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>创建通知公告请求（POST /api/system/notice）。</summary>
public class NoticeCreateRequest : NoticeWriteRequest
{
}

/// <summary>更新通知公告请求（PUT /api/system/notice/{id}，字段与创建一致）。</summary>
public class NoticeUpdateRequest : NoticeWriteRequest
{
}

/// <summary>最新通知公告简项（GET /api/system/notice/latest 返回项，不含正文，供首页公告卡片展示）。</summary>
public class NoticeLatestVO : EntityVO
{
    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>公告类型（见 <see cref="Domain.Shared.Enums.NoticeType"/>）。</summary>
    public NoticeType NoticeType { get; set; }
}
