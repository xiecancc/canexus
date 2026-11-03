using Application.Contracts.Dtos.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Files;
/// <summary>文件分页查询请求（GET /api/system/file/page）。</summary>
public class FilePageRequest : AggregatePagedRequest
{
    /// <summary>原始文件名：模糊匹配。</summary>
    public string? FileName { get; set; }

    /// <summary>业务用途分类：精确匹配。</summary>
    public string? BizCategory { get; set; }
}

/// <summary>文件分页数据项响应。</summary>
public class FilePagedResponse : AggregatePagedResponse
{
    /// <summary>原始文件名（含扩展名）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型 MIME。</summary>
    public string? ContentType { get; set; }

    /// <summary>文件大小（字节）。</summary>
    public long SizeBytes { get; set; }

    /// <summary>存储提供方标识。</summary>
    public string StorageProvider { get; set; } = string.Empty;

    /// <summary>业务用途分类。</summary>
    public string? BizCategory { get; set; }
}

/// <summary>文件上传成功响应。</summary>
public class FileUploadedResponse
{
    /// <summary>文件 Id（后续下载 / 查询定位）。</summary>
    public Guid Id { get; set; }

    /// <summary>原始文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long SizeBytes { get; set; }
}

/// <summary>文件详情响应（下载接口的元数据部分）。</summary>
public class FileDetailResponse : AggregateDetailResponse
{
    /// <summary>原始文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型 MIME。</summary>
    public string? ContentType { get; set; }

    /// <summary>文件大小（字节）。</summary>
    public long SizeBytes { get; set; }

    /// <summary>相对存储根目录的路径（下载流由处理器配套返回）。</summary>
    public string RelativePath { get; set; } = string.Empty;
}
