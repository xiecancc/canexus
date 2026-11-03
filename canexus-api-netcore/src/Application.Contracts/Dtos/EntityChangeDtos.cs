using Application.Contracts.Dtos.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Audit;
/// <summary>实体变更日志分页查询请求（GET /api/system/entity-change/page）。</summary>
public class EntityChangeLogPageRequest : AggregatePagedRequest
{
    /// <summary>实体类型名：模糊匹配（如 SysUser）。</summary>
    public string? EntityName { get; set; }

    /// <summary>实体主键 Id：精确匹配。</summary>
    public Guid? EntityId { get; set; }

    /// <summary>字段名：模糊匹配。</summary>
    public string? FieldName { get; set; }
}

/// <summary>实体变更日志列表行（分页数据项）。</summary>
public class EntityChangeLogPagedResponse : AggregatePagedResponse
{
    /// <summary>变更的实体类型名。</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>变更的实体主键 Id。</summary>
    public Guid EntityId { get; set; }

    /// <summary>变更的字段名。</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>变更前的值。</summary>
    public string? OldValue { get; set; }

    /// <summary>变更后的值。</summary>
    public string? NewValue { get; set; }

    /// <summary>变更时间。</summary>
    public DateTime? ChangedAt { get; set; }

    /// <summary>变更人（用户 Id）；系统任务为 null。</summary>
    public Guid? ChangedBy { get; set; }
}
