using Application.Contracts.Dtos.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 聚合查询抽象基类（带载荷）：携带载荷 DTO 的读操作请求，载荷经 <see cref="Request"/> 暴露。
/// <para>直接实现 <see cref="Request{TResponse}"/>（MediatR 带返回值请求根）；
/// 分页 / 列表等带载荷查询统一以本类为根，详情查询（载荷即聚合 Id）为其特殊形态。</para>
/// </summary>
/// <typeparam name="TRequest">载荷类型。</typeparam>
/// <typeparam name="TResponse">查询返回的数据类型。</typeparam>
/// <param name="request">查询载荷。</param>
public abstract record AggregateQuery<TRequest, TResponse>(TRequest request) : Request<TResponse>
{
    /// <summary>查询载荷。</summary>
    public TRequest Request { get; } = request;
}

/// <summary>
/// 聚合分页查询抽象基类（Aggregate 级）：载荷与响应约束到聚合分页请求 / 响应基类，
/// 响应统一为 <see cref="PagedResponse{T}"/> 包装（与列表查询的「项类型 + 包装响应」约定一致）。
/// </summary>
/// <typeparam name="TRequest">分页请求类型（须继承 <see cref="AggregatePagedRequest"/>，页参数由 setter 越界收敛）。</typeparam>
/// <typeparam name="TResponse">分页数据项 Response 类型（须继承 <see cref="AggregatePagedResponse"/>）。</typeparam>
/// <param name="Request">分页与筛选请求。</param>
public abstract record AggregateQueryPaged<TRequest, TResponse>(TRequest Request) : AggregateQuery<TRequest, PagedResponse<TResponse>>(Request)
    where TResponse : AggregatePagedResponse
    where TRequest : AggregatePagedRequest;

/// <summary>
/// 聚合列表查询抽象基类（Aggregate 级）：载荷约束到聚合列表请求基类，
/// <typeparamref name="TResponse"/> 为列表数据项类型，响应统一为 <c>List&lt;TResponse&gt;</c>
/// （与 <see cref="AggregateQueryPaged{TRequest, TResponse}"/> 的「项类型 + 包装响应」约定一致）。
/// </summary>
/// <typeparam name="TRequest">列表请求类型（须继承 <see cref="AggregateListRequest"/>）。</typeparam>
/// <typeparam name="TResponse">列表数据项 Response 类型（须继承 <see cref="AggregateItemResponse"/>）。</typeparam>
/// <param name="Request">列表查询载荷。</param>
public abstract record AggregateQueryList<TRequest, TResponse>(TRequest Request) : AggregateQuery<TRequest, List<TResponse>>(Request)
    where TResponse : AggregateItemResponse
    where TRequest : AggregateListRequest;

/// <summary>
/// 聚合下拉查询抽象基类（Aggregate 级）：查询全量下拉简项，载荷预留为后续筛选条件的挂载点
/// （当前处理器钩子不消费），过滤条件（仅启用项等）与排序由处理器钩子决定，响应约束到下拉简项基类。
/// </summary>
/// <typeparam name="TRequest">下拉请求类型（须继承 <see cref="AggregateDropdownRequest"/>）。</typeparam>
/// <typeparam name="TResponse">下拉简项响应类型（须继承 <see cref="AggregateDropdownResponse"/>）。</typeparam>
/// <param name="Request">下拉查询载荷（预留）。</param>
public abstract record AggregateQueryDropdown<TRequest, TResponse>(TRequest Request) : AggregateQuery<TRequest, List<TResponse>>(Request)
    where TResponse : AggregateDropdownResponse
    where TRequest : AggregateDropdownRequest;

/// <summary>
/// 聚合详情查询抽象基类：按 Id 查询单个聚合的公共形状（载荷即聚合 Id）。
/// <para>由 <c>QueryDetailHandler</c> 消费（直接读取 Id，子类无需再实现取值钩子）；
/// 详情查询 record 继承本基类并转发构造（<c>base(id)</c>）。</para>
/// <para>载荷即聚合 Id，同时以位置属性 <see cref="Id"/>（主读形态）与
/// 继承的 <c>request</c> 属性暴露，两者等值。</para>
/// <para>响应约束到 <see cref="AggregateDetailResponse"/>：详情读模型以详情根为基（在分页最小根之上
/// 补充租户归属与创建 / 更新审计），完整承载聚合标识与审计字段，支撑前端修改 / 删除的乐观锁回传闭环。</para>
/// </summary>
/// <typeparam name="TResponse">详情响应类型（须继承 <see cref="AggregateDetailResponse"/>）。</typeparam>
/// <param name="Id">目标聚合 Id。</param>
public abstract record AggregateQueryDetail<TResponse>(Guid Id) : AggregateQuery<Guid, TResponse>(Id)
    where TResponse : AggregateDetailResponse;
