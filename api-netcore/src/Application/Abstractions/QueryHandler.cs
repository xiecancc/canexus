
using Application.Contracts.Abstractions;

namespace Application.Abstractions;
/// <summary>
/// 查询处理器基类：继承 <see cref="RequestHandler{TRequest, TResponse}"/>，
/// 仅接受 <see cref="IQuery{TResponse}"/> 请求；查询特有的公共逻辑（如缓存、只读约束）在此层扩展。
/// </summary>
/// <typeparam name="TQuery">查询类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public abstract class QueryHandler<TQuery, TResponse> : RequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
}
