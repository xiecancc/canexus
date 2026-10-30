namespace Application.Abstractions;

using Application.Contracts.Abstractions;

/// <summary>
/// 请求处理器基类：封装 MediatR <c>IRequestHandler</c> 的适配逻辑（无返回值请求）。
/// <para>框架分发的公共入口 <see cref="Handle"/> 统一转发给子类实现的 <see cref="HandleAsync"/>，
/// 子类只关注业务本身；若未来替换 MediatR，仅需调整本抽象层。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
public abstract class RequestHandler<TRequest> : MediatR.IRequestHandler<TRequest>
    where TRequest : IRequest
{
    /// <inheritdoc />
    public Task Handle(TRequest request, CancellationToken cancellationToken)
    {
        return HandleAsync(request, cancellationToken);
    }

    /// <summary>子类实现的请求处理逻辑。</summary>
    /// <param name="request">请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected abstract Task HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 请求处理器基类：封装 MediatR <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> 的适配逻辑（带返回值请求）。
/// <para>框架分发的公共入口 <see cref="Handle"/> 统一转发给子类实现的 <see cref="HandleAsync"/>，
/// 子类只关注业务本身；若未来替换 MediatR，仅需调整本抽象层。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public abstract class RequestHandler<TRequest, TResponse> : MediatR.IRequestHandler<TRequest, TResponse>
    where TRequest : MediatR.IRequest<TResponse>
{
    /// <inheritdoc />
    public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        return HandleAsync(request, cancellationToken);
    }

    /// <summary>子类实现的请求处理逻辑。</summary>
    /// <param name="request">请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected abstract Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
