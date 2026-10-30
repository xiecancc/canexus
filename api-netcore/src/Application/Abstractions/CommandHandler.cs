namespace Application.Abstractions;

using Application.Contracts.Abstractions;

/// <summary>
/// 命令处理器基类（无返回值）：继承 <see cref="RequestHandler{TRequest}"/>，
/// 仅接受 <see cref="ICommand"/> 请求；命令特有的公共逻辑在此层扩展。
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
public abstract class CommandHandler<TCommand> : RequestHandler<TCommand>
    where TCommand : ICommand
{
}

/// <summary>
/// 命令处理器基类（带返回值）：继承 <see cref="RequestHandler{TRequest, TResponse}"/>，
/// 仅接受 <see cref="ICommand{TResponse}"/> 请求；命令特有的公共逻辑在此层扩展。
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public abstract class CommandHandler<TCommand, TResponse> : RequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
