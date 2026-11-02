using MediatR;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 请求标记根（无返回值形态）：无返回值 MediatR 请求（命令 / 查询）的公共标记，
/// 供处理器侧 <c>RequestHandler&lt;TRequest&gt;</c>、<c>CommandHandler&lt;TCommand&gt;</c> 泛型约束使用。
/// <para>重要：带返回值请求（<see cref="Request{TResponse}"/> 及其派生）不得继承本标记——
/// MediatR 12.4 的 <c>ISender</c> 同时提供标记重载 <c>Send&lt;TRequest&gt;(TRequest) where TRequest : IRequest</c>
/// 与带返回值重载 <c>Send&lt;TResponse&gt;(IRequest&lt;TResponse&gt;)</c>，
/// 请求若同时实现两者，重载决议会命中标记重载导致 <c>Send</c> 退化为无返回值（已实测验证）。</para>
/// </summary>
public abstract record Request : IRequest;

/// <summary>
/// 带返回值请求根：直接实现 <see cref="IRequest{TResponse}"/>（MediatR.Send 重载决议的硬性要求），
/// 不继承 <see cref="Request"/> 标记（避免 MediatR 非泛型 Send 重载二义，见上）。
/// <para>读操作请继承 <see cref="AggregateQuery{TRequest, TResponse}"/>（或其分页 / 列表 / 下拉 / 详情派生形态）；
/// 带返回值的命令（如登录签发令牌对）亦继承本基类。</para>
/// </summary>
/// <typeparam name="TResponse">请求返回的数据类型。</typeparam>
public abstract record Request<TResponse> : IRequest<TResponse>;
