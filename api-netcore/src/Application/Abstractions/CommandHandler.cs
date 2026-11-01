
using Application.Contracts.Abstractions;
using Application.Contracts.Common;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Abstractions;
/// <summary>
/// 命令处理器基类（无返回值）：继承 <see cref="RequestHandler{TRequest}"/>，
/// 仅接受 <see cref="Request"/> 标记的写命令请求；命令特有的公共逻辑在此层扩展。
/// <para>按约定本基类只应承载命令（Command 侧），查询请使用 <see cref="QueryHandler{TQuery, TResponse}"/>；
/// 标记约束无法在类型层面区分读 / 写，由命名与 Pipeline Behaviors（验证 / 日志 / 性能监控）共同保证。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
public abstract class CommandHandler<TCommand> : RequestHandler<TCommand>
    where TCommand : Request
{
}

/// <summary>
/// 命令处理器基类（带返回值）：继承 <see cref="RequestHandler{TRequest, TResponse}"/>，
/// 仅接受带返回值的写命令请求；命令特有的公共逻辑在此层扩展。
/// <para>约束到 <c>MediatR.IRequest&lt;TResponse&gt;</c> 而非专用命令基类：C# 单继承下，
/// 「标记基类 + 挂接响应接口」的命令形态（如 <c>DomainCreateCommand : Command&lt;TRequest&gt;, IRequest&lt;Guid&gt;</c>、
/// <c>AuthLoginCommand : Request&lt;TokenPairDto&gt;</c>）无法同时继承响应 record 基类，故以接口约束受理。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
public abstract class CommandHandler<TCommand, TResponse> : RequestHandler<TCommand, TResponse>
    where TCommand : MediatR.IRequest<TResponse>
{
    /// <summary>乐观锁校验：客户端回传版本号与聚合当前版本不一致时抛出并发冲突业务异常。</summary>
    /// <remarks>在业务守卫（NotFound / 保护规则 / 引用占用等）之后、任何写操作之前调用，
    /// 保证并发冲突与业务拒绝两种失败互不遮蔽。</remarks>
    /// <param name="entity">已加载的目标聚合。</param>
    /// <param name="expectedVersion">客户端回传的乐观锁版本号。</param>
    protected static void EnsureVersionMatches(AggregateBase entity, Guid expectedVersion)
    {
        if (entity.Version != expectedVersion)
        {
            throw new BusinessException(
                ErrorCodes.CommonConcurrencyConflict,
                "数据已被他人修改（版本号不一致），请刷新后重试");
        }
    }
}

/// <summary>
/// 领域创建命令处理器基类（对应 <see cref="DomainCreateCommand{TRequest}"/>）：封装
/// 「业务校验 → 请求映射 → 插入 → 发布创建事件 → 返回 bool」的标准创建流程（新聚合 Id 由数据访问层生成，不对外返回）。
/// 适用于单聚合、无事务与关联表编排的简单创建场景；复杂创建（工作单元事务、关联全删全插、密码哈希等旁路逻辑）
/// 应直接继承 <see cref="CommandHandler{TCommand, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">创建命令类型。</typeparam>
/// <typeparam name="TRequest">创建请求类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class DomainCreateCommandHandler<TCommand, TRequest, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : CommandHandler<TCommand, bool>
    where TCommand : DomainCreateCommand<TRequest>
    where TRequest : DomainChangeRequest
    where TEntity : AggregateBase
{
    /// <summary>目标聚合仓储。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>领域事件发布器。</summary>
    protected IDomainEventPublisher EventPublisher { get; } = eventPublisher;

    /// <summary>业务校验钩子：唯一性、关联存在性等前置校验（无校验时实现为空操作）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task ValidateAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>请求 → 聚合映射钩子（AutoMapper 映射或手工构造均可）。</summary>
    /// <param name="request">创建请求。</param>
    /// <returns>待插入的聚合。</returns>
    protected abstract TEntity MapToEntity(TRequest request);

    /// <summary>创建完成事件钩子：构造携带创建后聚合状态的领域事件。</summary>
    /// <param name="inserted">插入后的聚合（已生成主键与审计字段）。</param>
    /// <returns>待发布的领域事件。</returns>
    protected abstract DomainEvent CreateEvent(TEntity inserted);

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await ValidateAsync(command.Request, cancellationToken);
        var entity = MapToEntity(command.Request);
        _ = await Repository.InsertAsync(entity, cancellationToken);

        // 关键节点发布创建领域事件（主键与审计字段已就地填充到传入实体，携带创建后状态；
        // 缓存清理等响应动作由监听器处理）
        await EventPublisher.PublishAsync(CreateEvent(entity), cancellationToken);
        return true;
    }
}

/// <summary>
/// 聚合创建命令处理器基类（对应 <see cref="AggregateCreateCommand{TRequest}"/>）：
/// 继承领域创建处理器，实体与载荷约束收紧到聚合基类（主键 / 版本号 / 审计字段由数据访问层初始化）。
/// </summary>
/// <typeparam name="TCommand">创建命令类型。</typeparam>
/// <typeparam name="TRequest">创建请求类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateCreateCommandHandler<TCommand, TRequest, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : DomainCreateCommandHandler<TCommand, TRequest, TEntity>(repository, eventPublisher)
    where TCommand : AggregateCreateCommand<TRequest>
    where TRequest : AggregateChangeRequest
    where TEntity : AggregateBase
{
}

/// <summary>
/// 聚合更新命令处理器基类（对应 <see cref="AggregateUpdateCommand{TRequest}"/>）：封装
/// 「按 Id 取值兜底 → 业务校验（含乐观锁版本比对，由 <see cref="ValidateAsync"/> 钩子末尾调用
/// <see cref="EnsureVersionMatches"/>）→ 捕获变更前快照 → 请求原位映射 → 乐观并发更新 → 发布修改事件」
/// 的标准更新流程。适用于单聚合原位更新；涉及关联表替换或事务编排的更新应直接继承
/// <see cref="CommandHandler{TCommand, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">更新命令类型。</typeparam>
/// <typeparam name="TRequest">更新请求类型（携带乐观锁版本号 Version，由钩子负责比对）。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateUpdateCommandHandler<TCommand, TRequest, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : CommandHandler<TCommand, bool>
    where TCommand : AggregateUpdateCommand<TRequest>
    where TRequest : AggregateChangeRequest
    where TEntity : AggregateBase
{
    /// <summary>目标聚合仓储。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>领域事件发布器。</summary>
    protected IDomainEventPublisher EventPublisher { get; } = eventPublisher;

    /// <summary>模块 NotFound 错误码（ErrorCodes 常量）。</summary>
    protected abstract int NotFoundErrorCode { get; }

    /// <summary>实体显示名（NotFound 文案「{显示名} {id} 不存在」）。</summary>
    protected abstract string EntityDisplayName { get; }

    /// <summary>业务校验钩子：不可变字段、唯一性（排除自身）等前置校验（无校验时实现为空操作）。</summary>
    /// <param name="request">更新请求。</param>
    /// <param name="entity">已加载的目标聚合（可读取既有字段与 Id）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task ValidateAsync(TRequest request, TEntity entity, CancellationToken cancellationToken);

    /// <summary>请求字段 → 聚合原位映射钩子（直接修改 entity 属性或 AutoMapper 覆盖映射）。</summary>
    /// <param name="request">更新请求。</param>
    /// <param name="entity">待原位修改的聚合。</param>
    protected abstract void MapToEntity(TRequest request, TEntity entity);

    /// <summary>修改完成事件钩子：构造携带变更前后状态的领域事件。</summary>
    /// <param name="before">变更前快照。</param>
    /// <param name="after">变更后聚合。</param>
    /// <returns>待发布的领域事件。</returns>
    protected abstract DomainEvent CreateEvent(TEntity before, TEntity after);

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var entity = await Repository.GetOrThrowAsync(command.Id, NotFoundErrorCode, EntityDisplayName, cancellationToken);
        await ValidateAsync(command.Request, entity, cancellationToken);

        // 变更前捕获聚合快照（供修改事件携带变更前状态）
        var before = entity.Snapshot<TEntity>();

        MapToEntity(command.Request, entity);
        await Repository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布修改领域事件（变更比对等响应动作由监听器处理）
        await EventPublisher.PublishAsync(CreateEvent(before, entity), cancellationToken);
        return true;
    }
}

/// <summary>
/// 聚合删除命令处理器基类（对应 <see cref="AggregateDeleteCommand"/>）：封装
/// 「按 Id 取值兜底 → 删除前守卫 → 乐观锁版本比对 → 捕获删除前快照 → 软删除 → 发布删除事件」
/// 的标准删除流程（Id 与乐观锁版本号由删除命令直接携带）。适用于单聚合软删除；
/// 需级联清理关联表（事务）的删除应直接继承 <see cref="CommandHandler{TCommand, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">删除命令类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateDeleteCommandHandler<TCommand, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : CommandHandler<TCommand, bool>
    where TCommand : AggregateDeleteCommand
    where TEntity : AggregateBase
{
    /// <summary>目标聚合仓储。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>领域事件发布器。</summary>
    protected IDomainEventPublisher EventPublisher { get; } = eventPublisher;

    /// <summary>模块 NotFound 错误码（ErrorCodes 常量）。</summary>
    protected abstract int NotFoundErrorCode { get; }

    /// <summary>实体显示名（NotFound 文案「{显示名} {id} 不存在」）。</summary>
    protected abstract string EntityDisplayName { get; }

    /// <summary>删除前守卫钩子：内置数据保护、引用占用等拒绝删除的校验（默认放行）。</summary>
    /// <param name="entity">已加载的目标聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected virtual Task ValidateBeforeDeleteAsync(TEntity entity, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>删除完成事件钩子：构造携带删除前聚合状态的领域事件。</summary>
    /// <param name="deleted">删除前快照。</param>
    /// <returns>待发布的领域事件。</returns>
    protected abstract DomainEvent CreateEvent(TEntity deleted);

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var entity = await Repository.GetOrThrowAsync(command.Id, NotFoundErrorCode, EntityDisplayName, cancellationToken);
        await ValidateBeforeDeleteAsync(entity, cancellationToken);

        // 乐观锁校验：业务守卫通过后、任何写操作之前比对客户端回传版本（并发冲突与业务拒绝互不遮蔽）
        EnsureVersionMatches(entity, command.Version);

        // 变更前捕获聚合快照（软删除可能回填墓碑字段，事件携带删除前状态）
        var snapshot = entity.Snapshot<TEntity>();

        await Repository.DeleteAsync(command.Id, cancellationToken);

        // 关键节点发布删除领域事件（响应动作由监听器处理）
        await EventPublisher.PublishAsync(CreateEvent(snapshot), cancellationToken);
        return true;
    }
}

/// <summary>
/// 命令处理器仓储守卫扩展：聚合按 Id 取值兜底（统一 NotFound 语义）与唯一性校验。
/// <para>以扩展方法（组合）形式提供而非继承，供命令处理器与 CRUD 通用基类复用；
/// 错误码与文案由调用点显式传入，保持各模块错误语义集中可见。</para>
/// </summary>
internal static class CommandGuards
{
    /// <summary>按 Id 获取聚合，不存在时抛出统一格式的 NotFound 业务异常。</summary>
    /// <typeparam name="TEntity">聚合实体类型。</typeparam>
    /// <param name="repository">聚合仓储。</param>
    /// <param name="id">聚合 Id。</param>
    /// <param name="errorCode">模块 NotFound 错误码。</param>
    /// <param name="entityDisplayName">实体显示名（文案「{显示名} {id} 不存在」）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>聚合实体。</returns>
    public static async Task<TEntity> GetOrThrowAsync<TEntity>(
        this IAggregateRepository<TEntity> repository,
        Guid id,
        int errorCode,
        string entityDisplayName,
        CancellationToken cancellationToken = default)
        where TEntity : AggregateBase
    {
        return await repository.GetAsync([it => it.Id == id], cancellationToken: cancellationToken)
            ?? throw new BusinessException(errorCode, $"{entityDisplayName} {id} 不存在");
    }

    /// <summary>唯一性校验：存在满足条件的记录时抛出重复业务异常（需排除自身时把排除条件并入谓词）。</summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <param name="repository">实体仓储。</param>
    /// <param name="predicate">重复判定条件（AND 语义；更新场景应包含「Id != 自身」排除项）。</param>
    /// <param name="errorCode">模块重复错误码。</param>
    /// <param name="duplicateMessage">重复提示文案（含字段值的完整句子）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public static async Task EnsureNotDuplicateAsync<TEntity>(
        this IDomainRepository<TEntity> repository,
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        int errorCode,
        string duplicateMessage,
        CancellationToken cancellationToken = default)
        where TEntity : DomainBase
    {
        if (await repository.CountAsync([predicate], cancellationToken) > 0)
        {
            throw new BusinessException(errorCode, duplicateMessage);
        }
    }
}

/// <summary>
/// 聚合恢复命令处理器基类（对应 <see cref="AggregateRestoreCommand"/>）：封装
/// 「按 Id 幂等恢复（解除软删除墓碑）→ 返回 bool」的标准恢复流程。
/// <para>恢复为幂等操作：主键不存在或未删除时静默忽略（仓储语义）；不发布领域事件。</para>
/// </summary>
/// <typeparam name="TCommand">恢复命令类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
public abstract class AggregateRestoreCommandHandler<TCommand, TEntity>(
    IAggregateRepository<TEntity> repository) : CommandHandler<TCommand, bool>
    where TCommand : AggregateRestoreCommand
    where TEntity : AggregateBase
{
    /// <summary>目标聚合仓储。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await Repository.RestoreAsync(command.Id, cancellationToken);
        return true;
    }
}
