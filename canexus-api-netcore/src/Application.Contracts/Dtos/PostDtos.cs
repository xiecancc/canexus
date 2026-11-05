using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Posts;
/// <summary>岗位分页查询请求（GET /api/system/post/page）。</summary>
public class PostPageRequest : AggregatePagedRequest
{
    /// <summary>岗位名称（模糊匹配）。</summary>
    public string? PostName { get; set; }

    /// <summary>岗位编码（模糊匹配）。</summary>
    public string? PostCode { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>岗位列表行（分页数据项）。</summary>
public class PostPagedResponse : AggregatePagedResponse
{
    /// <summary>岗位名称。</summary>
    public string PostName { get; set; } = string.Empty;

    /// <summary>岗位编码。</summary>
    public string PostCode { get; set; } = string.Empty;

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}

/// <summary>岗位详情（GET /api/system/post/{id}）。</summary>
public class PostDetailResponse : AggregateDetailResponse
{
    /// <summary>岗位名称。</summary>
    public string PostName { get; set; } = string.Empty;

    /// <summary>岗位编码。</summary>
    public string PostCode { get; set; } = string.Empty;

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}

/// <summary>岗位写入请求（POST / PUT 共用载荷；镜像 SysPost 聚合的启停 / 备注与业务字段）。</summary>
public class PostInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>岗位名称（必填，不超过 50 字符）。</summary>
    public string PostName { get; set; } = string.Empty;

    /// <summary>岗位编码（必填，不超过 64 字符，唯一）。</summary>
    public string PostCode { get; set; } = string.Empty;

    /// <summary>显示顺序（升序，缺省 0）。</summary>
    public int OrderNum { get; set; }
}

/// <summary>岗位下拉请求（载荷预留为后续筛选条件的挂载点，当前无过滤字段）。</summary>
public class PostDropdownRequest : AggregateDropdownRequest
{
}

/// <summary>岗位下拉简项（Id + 展示文本，仅启用岗位）。</summary>
public class PostDropdownResponse : AggregateDropdownResponse
{
    /// <summary>岗位编码（下拉展示文本，配合名称定位）。</summary>
    public string PostCode { get; set; } = string.Empty;

    /// <summary>岗位名称（下拉展示文本）。</summary>
    public string PostName { get; set; } = string.Empty;

    /// <summary>显示顺序（前端排序兜底，服务端已按升序返回）。</summary>
    public int OrderNum { get; set; }
}
