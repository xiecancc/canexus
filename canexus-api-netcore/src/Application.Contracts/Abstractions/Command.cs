using Application.Contracts.Dtos.Common;
using MediatR;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 命令标记根（无载荷、无返回值）：无返回值写命令的公共标记，
/// 供处理器侧 <c>CommandHandler&lt;TCommand&gt;</c> 泛型约束使用。
/// <para>适用于无独立载荷 DTO 的简单写命令（状态切换 / 触发执行 / 清理等，
/// 参数直接以 record 位置属性承载，如 <c>UserResetPasswordCommand(Guid Id, UserPasswordRequest Request) : Command</c>）。
/// 带载荷的命令请继承 <see cref="Command{TRequest}"/>（响应统一为 bool）；
/// 带返回值的命令请继承 <see cref="Request{TResponse}"/>（如登录签发令牌对）。
/// 注意：带返回值请求不得继承本标记——MediatR 12.4 非泛型 Send 的标记重载会优先命中，
/// 导致响应退化为 void（详见 <see cref="Request"/> 说明）。</para>
/// </summary>
public abstract record Command : Request;

/// <summary>
/// 命令载荷抽象基类：携带请求载荷的写命令公共形状（创建 / 更新共用；
/// 删除 / 恢复命令为无载荷形态，见 <see cref="AggregateDeleteCommand"/> / <see cref="AggregateRestoreCommand"/>），
/// 继承 <see cref="Request{bool}"/> 后响应统一为 bool（写操作成功与否，不返回业务数据 / 新聚合 Id），
/// 供 <c>WriteCommandValidator</c> 等按载荷统一处理的组件约束使用。
/// <para>载荷经 <see cref="Command{TRequest}.Request"/> 属性暴露；由 MediatR 分发到对应的命令处理器，
/// 经由 Pipeline Behaviors（验证 / 日志 / 性能监控）处理后返回统一响应。</para>
/// </summary>
/// <typeparam name="TRequest">请求载荷类型。</typeparam>
/// <param name="request">请求载荷。</param>
public abstract record Command<TRequest>(TRequest request) : Request<bool>
{
    /// <summary>请求载荷。</summary>
    public TRequest Request { get; } = request;
}

/// <summary>
/// 标准创建命令抽象基类（Aggregate 级）：携带创建请求载荷，响应为 bool（写操作成功与否，不返回新聚合 Id）；
/// 载荷约束收紧到聚合请求基类（聚合 Id、乐观锁版本号与审计字段由数据访问层生成，不随载荷提交）。
/// <para>由 <c>CreateCommandHandler&lt;TCommand, TRequest, TEntity&gt;</c> 消费；
/// 创建命令 record 继承本基类并转发构造（<c>base(request)</c>）。</para>
/// </summary>
/// <typeparam name="TRequest">创建请求类型（须继承 <see cref="AggregateChangeRequest"/>）。</typeparam>
/// <param name="request">创建请求载荷。</param>
public abstract record AggregateCreateCommand<TRequest>(TRequest request) : Command<TRequest>(request)
    where TRequest : AggregateChangeRequest;

/// <summary>
/// 标准更新命令抽象基类：携带目标聚合 Id 与更新请求载荷，响应为 bool。
/// <para>由 <c>UpdateCommandHandler&lt;TCommand, TRequest, TEntity&gt;</c> 消费
/// （乐观锁版本号由载荷 DTO 携带，经 <see cref="Command{TRequest}.Request"/> 读取）；
/// 更新命令 record 继承本基类并转发构造（<c>base(id, request)</c>）。</para>
/// </summary>
/// <typeparam name="TRequest">更新请求类型。</typeparam>
/// <param name="id">目标聚合 Id。</param>
/// <param name="request">更新请求载荷。</param>
public abstract record AggregateUpdateCommand<TRequest>(Guid id, TRequest request) : Command<TRequest>(request)
    where TRequest : AggregateChangeRequest
{
    /// <summary>目标聚合 Id。</summary>
    public Guid Id { get; } = id;
}

/// <summary>
/// 标准删除命令抽象基类：参数为目标聚合 Id 与乐观锁版本号
/// （二者共同锁定唯一的聚合状态，防止「读到旧数据后误删新数据」），响应统一为 bool。
/// <para>由 <c>DeleteCommandHandler&lt;TCommand, TEntity&gt;</c> 消费（版本号用于删除前的乐观锁校验）；
/// 删除命令 record 继承本基类并转发构造（<c>base(id, version)</c>）。</para>
/// </summary>
/// <param name="Id">目标聚合 Id。</param>
/// <param name="Version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
public abstract record AggregateDeleteCommand(Guid Id, Guid Version) : Request<bool>;

/// <summary>
/// 标准恢复命令抽象基类：参数为目标聚合 Id 与乐观锁版本号
/// （须与聚合删除时固化的版本一致才允许恢复），响应统一为 bool。
/// <para>由恢复命令处理器消费（幂等语义：聚合不存在或未删除时静默忽略）；
/// 恢复命令 record 继承本基类并转发构造（<c>base(id, version)</c>）。</para>
/// </summary>
/// <param name="Id">目标聚合 Id。</param>
/// <param name="Version">乐观锁版本号（须与聚合删除时固化的版本一致才允许恢复）。</param>
public abstract record AggregateRestoreCommand(Guid Id, Guid Version) : Request<bool>;
