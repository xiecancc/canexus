
using System.Linq.Expressions;
using Application.Contracts.Abstractions;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;

namespace Application.Abstractions;
/// <summary>
/// 查询处理器基类：继承 <see cref="RequestHandler{TRequest, TResponse}"/>，
/// 仅接受带返回值的读请求（<see cref="Request{TResponse}"/> 派生，含无载荷查询与
/// <see cref="AggregateQuery{TRequest, TResponse}"/> 聚合派生）；查询特有的公共逻辑（如缓存、只读约束）在此层扩展。
/// </summary>
/// <typeparam name="TQuery">查询类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public abstract class QueryHandler<TQuery, TResponse> : RequestHandler<TQuery, TResponse>
    where TQuery : Request<TResponse>
{
    /// <summary>规整筛选文本：空白（null / 空串 / 全空白）返回 null 表示不过滤，否则返回去除首尾空白后的值。</summary>
    /// <param name="text">原始筛选文本。</param>
    /// <returns>规整后的筛选文本；不过滤时为 null。</returns>
    protected static string? NormalizeFilter(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

/// <summary>
/// 聚合分页查询处理器基类（对应 <see cref="AggregateQueryPaged{TRequest, TResponse}"/>）：封装
/// 「构造过滤条件 → 通用仓储分页（自动附加租户隔离与软删除过滤）→ 开放泛型映射闭合响应」的标准分页查询流程
/// （排序走通用仓储默认语义）。需要自定义排序、数据范围过滤或关联数据批量组装的分页查询应直接继承
/// <see cref="QueryHandler{TQuery, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TQuery">分页查询类型。</typeparam>
/// <typeparam name="TRequest">分页请求类型（须继承 <see cref="AggregatePagedRequest"/>）。</typeparam>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
/// <typeparam name="TResponse">分页数据项响应类型（须继承 <see cref="AggregatePagedResponse"/>）。</typeparam>
/// <param name="repository">聚合仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public abstract class AggregateQueryPagedHandler<TQuery, TRequest, TEntity, TResponse>(
    IAggregateRepository<TEntity> repository,
    IMapper mapper) : QueryHandler<TQuery, PagedResponse<TResponse>>
    where TQuery : AggregateQueryPaged<TRequest, TResponse>
    where TRequest : AggregatePagedRequest
    where TResponse : AggregatePagedResponse
    where TEntity : AggregateBase
{
    /// <summary>聚合仓储（只读）。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>AutoMapper 映射器。</summary>
    protected IMapper Mapper { get; } = mapper;

    /// <summary>过滤条件构造钩子：多条件 AND 组合（返回 null 表示不过滤）。</summary>
    /// <param name="request">分页与筛选请求。</param>
    /// <returns>过滤条件表达式；不过滤时为 null。</returns>
    protected abstract Expression<Func<TEntity, bool>>? BuildPredicate(TRequest request);

    /// <inheritdoc />
    protected sealed override async Task<PagedResponse<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var predicate = BuildPredicate(request);
        var page = await Repository.GetPagedAsync(request, predicate is null ? null : [predicate], cancellationToken: cancellationToken);

        // 依赖 CommonProfile 的开放泛型映射闭合 PagedResponse<聚合> → PagedResponse<响应>：
        // 分页元数据同名复制，Items 元素在运行期按各模块「聚合 → Response」映射解析
        return Mapper.Map<PagedResponse<TResponse>>(page)!;
    }
}

/// <summary>
/// 聚合详情查询处理器基类（对应 <see cref="AggregateQueryDetail{TResponse}"/>）：封装
/// 「按 Id 取值兜底 → 投影响应 → 可选组装扩展字段」的标准详情查询流程。
/// 扩展字段（如关联集合、正文回填）在 <see cref="EnrichAsync"/> 钩子中组装。
/// </summary>
/// <typeparam name="TQuery">详情查询类型。</typeparam>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
/// <typeparam name="TResponse">详情响应类型（须继承 <see cref="AggregateDetailResponse"/>）。</typeparam>
/// <param name="repository">聚合仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public abstract class AggregateQueryDetailHandler<TQuery, TEntity, TResponse>(
    IAggregateRepository<TEntity> repository,
    IMapper mapper) : QueryHandler<TQuery, TResponse>
    where TQuery : AggregateQueryDetail<TResponse>
    where TResponse : AggregateDetailResponse
    where TEntity : AggregateBase
{
    /// <summary>聚合仓储（只读）。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>AutoMapper 映射器。</summary>
    protected IMapper Mapper { get; } = mapper;

    /// <summary>模块 NotFound 错误码（ErrorCodes 常量）。</summary>
    protected abstract int NotFoundErrorCode { get; }

    /// <summary>实体显示名（NotFound 文案「{显示名} {id} 不存在」）。</summary>
    protected abstract string EntityDisplayName { get; }

    /// <summary>响应扩展字段组装钩子（默认不组装；如关联集合、映射忽略的正文等）。</summary>
    /// <param name="response">已映射的响应。</param>
    /// <param name="entity">聚合实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected virtual Task EnrichAsync(TResponse response, TEntity entity, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected sealed override async Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var entity = await Repository.GetOrThrowAsync(query.Id, NotFoundErrorCode, EntityDisplayName, cancellationToken);
        var response = Mapper.Map<TResponse>(entity)!;
        await EnrichAsync(response, entity, cancellationToken);
        return response;
    }
}

/// <summary>
/// 聚合列表查询处理器基类（对应 <see cref="AggregateQueryList{TRequest, TResponse}"/>）：封装
/// 「构造过滤条件 → 仓储条件查询（自动附加租户隔离与软删除过滤）→ 集合映射」的标准列表查询流程。
/// 需要关联数据批量组装的列表查询应直接继承 <see cref="QueryHandler{TQuery, List{TResponse}}"/> 显式实现。
/// </summary>
/// <typeparam name="TQuery">列表查询类型。</typeparam>
/// <typeparam name="TRequest">列表请求类型（须继承 <see cref="AggregateListRequest"/>）。</typeparam>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
/// <typeparam name="TResponse">列表数据项响应类型（须继承 <see cref="AggregateItemResponse"/>）。</typeparam>
/// <param name="repository">聚合仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public abstract class AggregateQueryListHandler<TQuery, TRequest, TEntity, TResponse>(
    IAggregateRepository<TEntity> repository,
    IMapper mapper) : QueryHandler<TQuery, List<TResponse>>
    where TQuery : AggregateQueryList<TRequest, TResponse>
    where TRequest : AggregateListRequest
    where TResponse : AggregateItemResponse
    where TEntity : AggregateBase
{
    /// <summary>聚合仓储（只读）。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>AutoMapper 映射器。</summary>
    protected IMapper Mapper { get; } = mapper;

    /// <summary>过滤条件构造钩子：多条件 AND 组合（返回 null 表示不过滤）。</summary>
    /// <param name="request">列表请求。</param>
    /// <returns>过滤条件表达式；不过滤时为 null。</returns>
    protected abstract Expression<Func<TEntity, bool>>? BuildListPredicate(TRequest request);

    /// <inheritdoc />
    protected sealed override async Task<List<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var predicate = BuildListPredicate(query.Request);
        var entities = await Repository.GetListAsync(predicate is null ? null : [predicate], cancellationToken: cancellationToken);

        // 页内实体集合整体映射（元素映射运行期按各模块「聚合 → Response」注册解析）
        return Mapper.Map<List<TResponse>>(entities);
    }
}

/// <summary>
/// 聚合下拉查询处理器基类（对应 <see cref="AggregateQueryDropdown{TRequest, TResponse}"/>）：
/// 封装「钩子过滤（模块自行决定仅启用项等条件）→ 排序下推 → 集合映射」的标准下拉流程
/// （载荷预留为后续筛选条件的挂载点，当前过滤不消费）。
/// </summary>
/// <typeparam name="TQuery">下拉查询类型。</typeparam>
/// <typeparam name="TRequest">下拉请求类型（须继承 <see cref="AggregateDropdownRequest"/>）。</typeparam>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
/// <typeparam name="TResponse">下拉简项响应类型（须继承 <see cref="AggregateDropdownResponse"/>）。</typeparam>
/// <param name="repository">聚合仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public abstract class AggregateQueryDropdownHandler<TQuery, TRequest, TEntity, TResponse>(
    IAggregateRepository<TEntity> repository,
    IMapper mapper) : QueryHandler<TQuery, List<TResponse>>
    where TQuery : AggregateQueryDropdown<TRequest, TResponse>
    where TRequest : AggregateDropdownRequest
    where TResponse : AggregateDropdownResponse
    where TEntity : AggregateBase
{
    /// <summary>聚合仓储（只读）。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>AutoMapper 映射器。</summary>
    protected IMapper Mapper { get; } = mapper;

    /// <summary>过滤条件构造钩子：下拉项过滤（如仅启用项；返回 null 表示不过滤）。</summary>
    /// <returns>过滤条件表达式；不过滤时为 null。</returns>
    protected abstract Expression<Func<TEntity, bool>>? BuildDropdownPredicate();

    /// <summary>排序构造钩子：多重排序（value 为 true 升序、false 或 null 降序；返回 null 时按仓储默认序）。</summary>
    /// <returns>排序字典；默认为 null。</returns>
    protected virtual IDictionary<Expression<Func<TEntity, object>>, bool?>? BuildDropdownOrders() => null;

    /// <inheritdoc />
    protected sealed override async Task<List<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var predicate = BuildDropdownPredicate();
        var entities = await Repository.GetListAsync(predicate is null ? null : [predicate], BuildDropdownOrders(), cancellationToken);

        // 下拉实体集合整体映射（元素映射运行期按各模块「实体 → Response」注册解析）
        return Mapper.Map<List<TResponse>>(entities);
    }
}
