using Application.Contracts.Common;
using Domain.Shared.Paging;
using MediatR;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 查询抽象基类（无载荷）：封装一切不改变系统状态的读操作请求。
/// <para>适用于无独立载荷 DTO 的查询（下拉列表 / 树形 / 按标量参数查询等，
/// 查询参数直接以 record 位置属性承载）；带载荷 DTO 的查询请继承 <see cref="Query{TRequest, TResponse}"/>。</para>
/// <para>继承链经 <see cref="Request{TResponse}"/> 挂接 <see cref="IRequest{TResponse}"/>（MediatR.Send 重载决议的硬性要求）。</para>
/// </summary>
/// <typeparam name="TResponse">查询返回的数据类型。</typeparam>
public abstract record Query<TResponse> : Request<TResponse>;

/// <summary>
/// 查询抽象基类（带载荷）：携带载荷 DTO 的读操作请求，载荷经 <see cref="Request"/> 暴露。
/// <para>继承 <see cref="Query{TResponse}"/> 以满足处理器侧 <c>QueryHandler&lt;TQuery, TResponse&gt;</c> 的约束。</para>
/// </summary>
/// <typeparam name="TRequest">载荷类型。</typeparam>
/// <typeparam name="TResponse">查询返回的数据类型。</typeparam>
/// <param name="Request">查询载荷。</param>
public abstract record Query<TRequest, TResponse>(TRequest Request) : Query<TResponse>
{
    /// <summary>查询载荷。</summary>
    public TRequest Request { get; } = Request;
}

/// <summary>
/// 领域分页查询抽象基类（Domain 级）：载荷与响应分别约束到领域分页请求 / 响应基类。
/// </summary>
/// <typeparam name="TRequest">分页请求类型（须继承 <see cref="DomainPagedRequest"/>，页参数由 setter 越界收敛）。</typeparam>
/// <typeparam name="TResponse">分页数据项 Response 类型（须继承 <see cref="DomainPagedResponse"/>）。</typeparam>
/// <param name="Request">分页与筛选请求。</param>
public abstract record DomainQueryPaged<TRequest, TResponse>(TRequest Request) : Query<TRequest, PagedResponse<TResponse>>(Request)
    where TResponse : DomainPagedResponse
    where TRequest : DomainPagedRequest;

/// <summary>
/// 领域列表查询抽象基类（Domain 级）：载荷约束到领域列表请求基类，
/// <typeparamref name="TResponse"/> 为列表数据项类型，响应统一为 <c>List&lt;TResponse&gt;</c>
/// （与 <see cref="DomainQueryPaged{TRequest, TResponse}"/> 的「项类型 + 包装响应」约定一致）。
/// </summary>
/// <typeparam name="TRequest">列表请求类型（须继承 <see cref="DomainListRequest"/>）。</typeparam>
/// <typeparam name="TResponse">列表数据项 Response 类型（须继承 <see cref="DomainListResponse"/>）。</typeparam>
/// <param name="Request">列表查询载荷。</param>
public abstract record DomainQueryList<TRequest, TResponse>(TRequest Request) : Query<TRequest, List<TResponse>>(Request)
    where TResponse : DomainListResponse
    where TRequest : DomainListRequest;

/// <summary>
/// 聚合分页查询抽象基类（Aggregate 级）：继承领域分页查询，载荷与响应约束收紧到聚合基类。
/// </summary>
/// <typeparam name="TRequest">分页请求类型（须继承 <see cref="AggregatePagedRequest"/>）。</typeparam>
/// <typeparam name="TResponse">分页数据项 Response 类型（须继承 <see cref="AggregatePagedResponse"/>）。</typeparam>
/// <param name="Request">分页与筛选请求。</param>
public abstract record AggregateQueryPaged<TRequest, TResponse>(TRequest Request) : DomainQueryPaged<TRequest, TResponse>(Request)
    where TResponse : AggregatePagedResponse
    where TRequest : AggregatePagedRequest;

/// <summary>
/// 聚合列表查询抽象基类（Aggregate 级）：继承领域列表查询，载荷与响应约束收紧到聚合基类。
/// </summary>
/// <typeparam name="TRequest">列表请求类型（须继承 <see cref="AggregateListRequest"/>）。</typeparam>
/// <typeparam name="TResponse">列表数据项 Response 类型（须继承 <see cref="AggregateListResponse"/>）。</typeparam>
/// <param name="Request">列表查询载荷。</param>
public abstract record AggregateQueryList<TRequest, TResponse>(TRequest Request) : DomainQueryList<TRequest, TResponse>(Request)
    where TResponse : AggregateListResponse
    where TRequest : AggregateListRequest;

/// <summary>
/// 聚合详情查询抽象基类：按 Id 查询单个聚合的公共形状（载荷即聚合 Id）。
/// <para>由 <c>QueryByIdHandler</c> 消费（直接读取 Id，子类无需再实现取值钩子）；
/// 详情查询 record 继承本基类并转发构造（<c>base(id)</c>）。</para>
/// <para>载荷即聚合 Id，同时以位置属性 <see cref="Id"/>（主读形态）与
/// 继承的 <c>Request</c> 属性暴露，两者等值。</para>
/// <para>响应类型不设基类约束：详情响应的 DTO 家族由各模块自行选择
/// （当前模块均挂在 <see cref="AggregatePagedResponse"/> 家族下）。</para>
/// </summary>
/// <typeparam name="TResponse">详情响应类型。</typeparam>
/// <param name="Id">目标聚合 Id。</param>
public abstract record AggregateQueryById<TResponse>(Guid Id) : Query<Guid, TResponse>(Id)
{
}
