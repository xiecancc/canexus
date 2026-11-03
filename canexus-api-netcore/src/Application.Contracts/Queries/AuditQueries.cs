using Application.Contracts.Abstractions;
using Application.Contracts.Audit;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询实体变更日志（实体类型名 / 实体 Id / 字段名过滤）。</summary>
public sealed record EntityChangeLogQueryPaged : AggregateQueryPaged<EntityChangeLogPageRequest, EntityChangeLogPagedResponse>
{
    /// <summary>构造实体变更日志分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public EntityChangeLogQueryPaged(EntityChangeLogPageRequest request) : base(request) { }
}
