
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Audit;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Queries;
/// <summary>分页查询实体变更日志处理器（标准分页流程：实体类型名 / 实体 Id / 字段名过滤）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="repository">实体变更日志仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class EntityChangeLogQueryPagedHandler(IAggregateRepository<SysEntityChangeLog> repository, IMapper mapper)
    : AggregateQueryPagedHandler<EntityChangeLogQueryPaged, EntityChangeLogPageRequest, SysEntityChangeLog, EntityChangeLogPagedResponse>(repository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysEntityChangeLog, bool>>? BuildPredicate(EntityChangeLogPageRequest request)
    {
        var entityName = NormalizeFilter(request.EntityName);
        var fieldName = NormalizeFilter(request.FieldName);
        var entityId = request.EntityId;

        return l =>
            (entityName == null || l.EntityName.Contains(entityName))
            && (entityId == null || l.EntityId == entityId.Value)
            && (fieldName == null || l.FieldName.Contains(fieldName));
    }
}
