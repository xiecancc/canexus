namespace Application.Contracts.Abstractions;

/// <summary>
/// 命令标记接口：封装一切改变系统状态的写操作请求（创建 / 更新 / 删除 / 状态变更等），继承自 <see cref="IRequest"/>。
/// <para>由 MediatR 分发到对应的 CommandHandler，经由 Pipeline Behaviors（验证 / 日志 / 性能监控）处理后返回统一响应。</para>
/// </summary>
public interface ICommand : IRequest
{
}

/// <summary>
/// 带返回值的命令标记接口：写操作完成后需要返回数据（如新建实体的 Id）。
/// <para>直接继承 MediatR.IRequest&lt;TResponse&gt;：MediatR.Send 的重载决议要求请求接口
/// 直接继承其标记接口，任何自定义中间泛型接口都会导致类型推断失败。</para>
/// </summary>
/// <typeparam name="TResponse">命令返回的数据类型。</typeparam>
public interface ICommand<TResponse> : MediatR.IRequest<TResponse>
{
}
