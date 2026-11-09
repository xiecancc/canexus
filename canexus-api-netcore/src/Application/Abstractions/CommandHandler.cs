
using Application.Contracts.Abstractions;
using Application.Contracts.Dtos.Common;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Abstractions;
/// <summary>
/// 聚合写命令处理器基类：命令侧的根抽象，继承 <see cref="RequestHandler{TRequest, TResponse}"/>；
/// 响应统一为 bool（写操作成功与否，不返回聚合 Id），承载各聚合写命令（创建 / 更新 / 删除 / 恢复）
/// 共用的只读聚合仓储与领域事件发布器。
/// <para>乐观并发控制不在此层显式比对：实体更新由数据访问层经 SqlSugar ExecuteCommandWithOptLock
/// 做框架级版本比对（UPDATE ... WHERE Version = 传入值，受影响 0 行即冲突抛并发异常），
/// 软删除 / 恢复按仓储语义幂等执行并轮换版本令牌（使旧令牌持有的更新失效）。</para>
/// </summary>
/// <typeparam name="TCommand">聚合写命令类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateCommandHandler<TCommand, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : RequestHandler<TCommand, bool>
    where TCommand : MediatR.IRequest<bool>
    where TEntity : AggregateBase
{
    /// <summary>目标聚合仓储。</summary>
    protected IAggregateRepository<TEntity> Repository { get; } = repository;

    /// <summary>领域事件发布器（创建 / 更新 / 删除 / 恢复的关键节点事件均经此发布）。</summary>
    protected IDomainEventPublisher EventPublisher { get; } = eventPublisher;
}

/// <summary>
/// 聚合创建命令处理器基类（对应 <see cref="AggregateCreateCommand{TRequest}"/>）：封装
/// 「业务校验 → 请求映射 → 插入（主键 / 版本号 / 审计字段由数据访问层生成，不对外返回）→ 发布创建事件 → 返回 bool」
/// 的标准创建流程。适用于单聚合、无事务与关联表编排的简单创建场景；复杂创建（工作单元事务、关联全删全插、
/// 密码哈希等旁路逻辑）应直接继承 <see cref="RequestHandler{TRequest, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">创建命令类型。</typeparam>
/// <typeparam name="TRequest">创建请求类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateCreateCommandHandler<TCommand, TRequest, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : AggregateCommandHandler<TCommand, TEntity>(repository, eventPublisher)
    where TCommand : AggregateCreateCommand<TRequest>
    where TRequest : AggregateInputRequest
    where TEntity : AggregateBase
{
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
/// 聚合更新命令处理器基类（对应 <see cref="AggregateUpdateCommand{TRequest}"/>）：封装
/// 「按 Id 取值兜底 → 业务校验 → 捕获变更前快照 → 请求原位映射 → 乐观并发更新（SqlSugar 框架级
/// 版本比对，冲突抛并发异常）→ 发布修改事件」
/// 的标准更新流程。适用于单聚合原位更新；涉及关联表替换或事务编排的更新应直接继承
/// <see cref="RequestHandler{TRequest, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">更新命令类型。</typeparam>
/// <typeparam name="TRequest">更新请求类型（乐观锁版本号 Version 由数据访问层框架级比对）。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateUpdateCommandHandler<TCommand, TRequest, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : AggregateCommandHandler<TCommand, TEntity>(repository, eventPublisher)
    where TCommand : AggregateUpdateCommand<TRequest>
    where TRequest : AggregateInputRequest
    where TEntity : AggregateBase
{
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

        // 回填乐观锁版本令牌（命令参数独立携带，不随载荷映射；由 SqlSugar 框架级比对）
        entity.ApplyVersion(command.Version);

        await Repository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布修改领域事件（变更比对等响应动作由监听器处理）
        await EventPublisher.PublishAsync(CreateEvent(before, entity), cancellationToken);
        return true;
    }
}

/// <summary>
/// 聚合删除命令处理器基类（对应 <see cref="AggregateDeleteCommand"/>）：封装
/// 「按 Id 取值兜底 → 删除前守卫 → 捕获删除前快照 → 软删除（幂等执行并轮换版本令牌）→ 发布删除事件」
/// 的标准删除流程（Id 与乐观锁版本号由删除命令直接携带）。适用于单聚合软删除；
/// 需级联清理关联表（事务）的删除应直接继承 <see cref="RequestHandler{TRequest, TResponse}"/> 显式实现。
/// </summary>
/// <typeparam name="TCommand">删除命令类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateDeleteCommandHandler<TCommand, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : AggregateCommandHandler<TCommand, TEntity>(repository, eventPublisher)
    where TCommand : AggregateDeleteCommand
    where TEntity : AggregateBase
{
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

        // 变更前捕获聚合快照（软删除可能回填墓碑字段，事件携带删除前状态）
        var snapshot = entity.Snapshot<TEntity>();

        await Repository.SoftDeleteAsync(command.Id, cancellationToken, command.Version);

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

    /// <summary>回填乐观锁版本令牌：把客户端提交的版本号写入聚合（作为 SqlSugar 框架级比对的基准，
    /// 冲突时受影响 0 行抛并发异常）。版本号由命令参数独立承载，不随载荷映射。</summary>
    /// <typeparam name="TEntity">聚合实体类型。</typeparam>
    /// <param name="entity">待更新的聚合。</param>
    /// <param name="version">客户端提交的乐观锁版本号。</param>
    public static void ApplyVersion<TEntity>(this TEntity entity, Guid version)
        where TEntity : AggregateBase
    {
        entity.Version = version;
    }
}

/// <summary>
/// 聚合恢复命令处理器基类（对应 <see cref="AggregateRestoreCommand"/>）：封装
/// 「幂等恢复（解除软删除墓碑）→ 实际恢复时取回恢复后聚合并发布恢复事件 → 返回是否恢复」
/// 的标准恢复流程。
/// <para>恢复为幂等操作：主键不存在或未删除时仓储静默忽略并返回 false（不发布事件）；
/// 仅在墓碑实际解除后由 <see cref="CreateEvent"/> 构造事件发布（携带恢复后聚合状态，
/// 供缓存失效等监听器响应）。</para>
/// </summary>
/// <typeparam name="TCommand">恢复命令类型。</typeparam>
/// <typeparam name="TEntity">目标聚合类型。</typeparam>
/// <param name="repository">目标聚合仓储。</param>
/// <param name="eventPublisher">领域事件发布器。</param>
public abstract class AggregateRestoreCommandHandler<TCommand, TEntity>(
    IAggregateRepository<TEntity> repository,
    IDomainEventPublisher eventPublisher) : AggregateCommandHandler<TCommand, TEntity>(repository, eventPublisher)
    where TCommand : AggregateRestoreCommand
    where TEntity : AggregateBase
{
    /// <summary>恢复完成事件钩子：构造携带恢复后聚合状态的领域事件。</summary>
    /// <param name="restored">恢复后的聚合（已解除软删除墓碑）。</param>
    /// <returns>待发布的领域事件。</returns>
    protected abstract DomainEvent CreateEvent(TEntity restored);

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        // 幂等恢复：主键不存在或未删除时仓储静默忽略（返回 false），不发布事件
        if (!await Repository.RestoreAsync(command.Id, cancellationToken, command.Version))
        {
            return false;
        }

        // 恢复刚成功，聚合必然可常规查询（墓碑已解除）；取回恢复后状态供事件携带
        var restored = await Repository.GetAsync([it => it.Id == command.Id], cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"聚合 {command.Id} 恢复成功但不可查询，疑似数据访问层软删除过滤异常");

        // 关键节点发布恢复领域事件（缓存失效等响应动作由监听器处理）
        await EventPublisher.PublishAsync(CreateEvent(restored), cancellationToken);
        return true;
    }
}
