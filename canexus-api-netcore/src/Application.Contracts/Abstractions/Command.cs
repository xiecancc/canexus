using Application.Contracts.Dtos.Common;
using MediatR;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 写命令标记：事务管道（TransactionBehavior）据此对所有写命令自动包裹数据库事务
/// （成功提交 / 异常回滚），查询请求不得实现本标记。
/// <para>刻意不继承 MediatR 的 <see cref="IRequest"/>（避免与 <see cref="Request{TResponse}"/>
/// 的重载决议冲突，见 <see cref="Request"/> 文档）——本标记仅供管道做类型判定，不参与 MediatR 分发。</para>
/// </summary>
public interface ICommand
{
}

/// <summary>
/// 聚合写命令抽象基类（带载荷）：携带载荷 DTO 的写操作请求（响应统一为 bool，写操作成功与否），
/// 载荷经 <see cref="Request"/> 暴露；创建 / 更新命令统一以本类为根，与查询侧
/// <see cref="AggregateQuery{TRequest, TResponse}"/> 的「根 + 载荷属性」形态对称。
/// <para>直接实现 <see cref="Request{TResponse}"/>，不继承非泛型标记 <see cref="Request"/>——
/// 避免同时实现 <c>IRequest</c> 与 <c>IRequest&lt;bool&gt;</c> 触发 MediatR.Send 重载决议退化（见 <see cref="Request"/> 文档）。</para>
/// </summary>
/// <typeparam name="TRequest">写命令载荷类型。</typeparam>
/// <param name="request">写命令载荷。</param>
public abstract record AggregateCommand<TRequest>(TRequest request) : Request<bool>, ICommand
{
    /// <summary>写命令载荷。</summary>
    public TRequest Request { get; } = request;
}

/// <summary>
/// 标准创建命令抽象基类（Aggregate 级）：携带创建请求载荷，响应为 bool（写操作成功与否，不返回新聚合 id）；
/// 载荷约束收紧到聚合请求基类（聚合 id、乐观锁版本号与审计字段由数据访问层生成，不随载荷提交）。
/// <para>由 <c>AggregateCreateCommandHandler&lt;TCommand, TRequest, TEntity&gt;</c> 消费；
/// 创建命令 record 继承本基类并转发构造（<c>base(request)</c>）。</para>
/// </summary>
/// <typeparam name="TRequest">创建请求类型（须继承 <see cref="AggregateInputRequest"/>）。</typeparam>
/// <param name="request">创建请求载荷。</param>
public abstract record AggregateCreateCommand<TRequest>(TRequest request) : AggregateCommand<TRequest>(request)
    where TRequest : AggregateInputRequest
{
}

/// <summary>
/// 标准更新命令抽象基类：携带目标聚合 id、乐观锁版本号与更新请求载荷，响应为 bool。
/// <para>乐观锁版本号与聚合 id 同级独立承载（不进载荷 DTO），与删除命令形态对称，
/// 传输层统一以路由参数（<c>{id}/{version}</c>）回传；由
/// <c>AggregateUpdateCommandHandler&lt;TCommand, TRequest, TEntity&gt;</c> 消费
/// （版本号由 SqlSugar 框架级比对，冲突抛并发异常）；
/// 更新命令 record 继承本基类并转发构造（<c>base(id, version, request)</c>）。</para>
/// </summary>
/// <typeparam name="TRequest">更新请求类型。</typeparam>
/// <param name="id">目标聚合 id。</param>
/// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许修改，由处理器回填实体后框架级比对）。</param>
/// <param name="request">更新请求载荷。</param>
public abstract record AggregateUpdateCommand<TRequest>(Guid id, Guid version, TRequest request) : AggregateCommand<TRequest>(request)
    where TRequest : AggregateInputRequest
{
    /// <summary>目标聚合 id。</summary>
    public Guid Id { get; } = id;

    /// <summary>乐观锁版本号（修改令牌，须与聚合当前版本一致才允许修改）。</summary>
    public Guid Version { get; } = version;
}

/// <summary>
/// 标准删除命令抽象基类：参数为目标聚合 id 与乐观锁版本号
/// （二者共同锁定唯一的聚合状态，防止「读到旧数据后误删新数据」），响应统一为 bool。
/// <para>由 <c>AggregateDeleteCommandHandler&lt;TCommand, TEntity&gt;</c> 消费（版本号用于删除前的乐观锁校验）；
/// 删除命令 record 继承本基类并转发构造（<c>base(id, version)</c>）。</para>
/// </summary>
/// <param name="id">目标聚合 id。</param>
/// <param name="version">乐观锁版本号（须与聚合当前版本一致才允许删除）。</param>
public abstract record AggregateDeleteCommand(Guid id, Guid version) : Request<bool>, ICommand
{
    /// <summary>目标聚合 id。</summary>
    public Guid Id { get; } = id;

    /// <summary>乐观锁版本号（删除令牌，须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; } = version;
}

/// <summary>
/// 标准恢复命令抽象基类：参数为目标聚合 id 与乐观锁版本号
/// （须与聚合删除时固化的版本一致才允许恢复），响应统一为 bool。
/// <para>由恢复命令处理器消费（幂等语义：聚合不存在或未删除时静默忽略）；
/// 恢复命令 record 继承本基类并转发构造（<c>base(id, version)</c>）。</para>
/// </summary>
/// <param name="id">目标聚合 id。</param>
/// <param name="version">乐观锁版本号（须与聚合删除时固化的版本一致才允许恢复）。</param>
public abstract record AggregateRestoreCommand(Guid id, Guid version) : Request<bool>, ICommand
{
    /// <summary>目标聚合 id。</summary>
    public Guid Id { get; } = id;

    /// <summary>乐观锁版本号（恢复令牌，须与聚合删除时固化的版本一致才允许恢复）。</summary>
    public Guid Version { get; } = version;
}
