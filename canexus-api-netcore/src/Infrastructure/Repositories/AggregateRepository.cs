
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.DataAccess;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar 的聚合仓储实现（对应 <see cref="AggregateBase"/>）：
/// 继承领域仓储（查询 / 插入），补充乐观并发更新能力；删除 / 恢复族（按主键 / 集合 / 条件）
/// 统一实现为软删除与解除墓碑（单条 SQL 按列更新，显式轮换版本令牌并填充更新人 / 更新时间）。
/// <para>审计字段（创建人与时间、软删除标记、实体更新的更新人与时间）由 Aop.DataExecuting 自动填充；
/// 按列更新路径绕过了该切面，故软删除 / 恢复在仓储内显式补齐更新审计与版本轮换（盲区兜底）；
/// 实体更新经 SqlSugar ExecuteCommandWithOptLock 做框架级乐观并发控制（UPDATE ... WHERE Version = 传入值，
/// 受影响 0 行即冲突），版本冲突时抛出业务异常（错误码 CommonConcurrencyConflict）。</para>
/// <para>CRUD 构建器复用领域仓储的查询 / 插入 / 更新 / 物理删除构建器；本类的 <c>SoftDeleteAsync</c> 族为软删除，
/// 经 <c>BuildUpdate</c> 产出的更新对象按列更新实现。基类继承的 <see cref="DomainRepository{TEntity}.HardDeleteAsync"/>
/// 仍是物理删除，两条通道显式分离、互不改写：派生仓储默认调用软删除，
/// 确需彻底清除数据时显式调用物理删除并自行承担不可恢复与审计缺失的风险。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文（软删除 / 恢复按列更新的更新审计人取值来源）。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class AggregateRepository<TEntity>(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext)
    : DomainRepository<TEntity>(client, tenantContext), IAggregateRepository<TEntity>
    where TEntity : AggregateBase, new()
{
    /// <summary>当前用户上下文（软删除 / 恢复按列更新的更新审计人取值来源；无登录上下文时为 null，
    /// 可空语义下不记录操作人）。</summary>
    protected IUserContext CurrentUser => currentUser;

    /// <summary>是否记录字段级变更日志（新增 / 更新 / 软删 / 恢复均写 sys_entity_change_log）；
    /// 高频自写表（如刷新令牌轮换）可重写为 false 关闭。</summary>
    protected virtual bool EntityChangeLogEnabled => true;

    /// <summary>变更日志开关（含自引用防护）：日志实体自身不得再产生变更日志，避免无限递归。</summary>
    private bool ChangeLogEnabled => EntityChangeLogEnabled && typeof(TEntity) != typeof(SysEntityChangeLog);

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 清除软删除 / 租户全局过滤器后按主键取墓碑，再按当前登录用户的租户范围显式约束，
        // 避免恢复前校验因默认查询过滤而读不到目标行，同时不放大跨租户读取范围。
        var query = BuildQuery()
            .ClearFilter<TEntity>()
            .Where(it => it.Id == id && it.IsDeleted);

        if (CurrentUser.TenantId is { } tenantId)
        {
            query = query.Where(it => it.TenantId == tenantId);
        }

        return await query.FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>插入成功后按字段补一条「新增」变更日志（OldValue 为 null，敏感字段值脱敏）；
    /// 与业务插入同连接，处于外层事务时随其原子提交。</remarks>
    public override async Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var inserted = await base.InsertAsync(entity, cancellationToken);
        if (!inserted || !ChangeLogEnabled)
        {
            return inserted;
        }

        var logs = EntityChangeLogger.BuildCreated(entity, DateTime.UtcNow, CurrentUser.UserId);
        if (logs.Count > 0)
        {
            await Client.Insertable(logs).ExecuteCommandAsync(cancellationToken);
        }

        return inserted;
    }

    /// <inheritdoc />
    /// <remarks>批量插入（如用户导入）同样逐实体补字段级「新增」变更日志。</remarks>
    public override async Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var materialized = entities as List<TEntity> ?? [.. entities];
        var inserted = await base.InsertAsync(materialized, cancellationToken);
        if (!inserted || !ChangeLogEnabled)
        {
            return inserted;
        }

        var now = DateTime.UtcNow;
        var userId = CurrentUser.UserId;
        var logs = materialized
            .SelectMany(entity => EntityChangeLogger.BuildCreated(entity, now, userId))
            .ToList();
        if (logs.Count > 0)
        {
            await Client.Insertable(logs).ExecuteCommandAsync(cancellationToken);
        }

        return inserted;
    }

    /// <inheritdoc />
    public virtual async Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 变更审计：更新前取数据库快照（与业务更新同连接，读到的即旧值），
        // 字段级变更日志与业务更新写入同一事务（事务回滚时日志一并回滚，不记录未发生的变更）
        var oldEntity = ChangeLogEnabled
            ? await BuildQuery().FirstAsync(it => it.Id == entity.Id, cancellationToken)
            : null;
        var changeLogs = EntityChangeLogger.Build(oldEntity, entity, DateTime.UtcNow, CurrentUser.UserId);

        // 框架级乐观锁（Version 标记 IsEnableUpdateVersionValidation）：ExecuteCommandWithOptLock 自动生成
        // UPDATE ... WHERE Id = @id AND Version = 传入值，成功后自动轮换版本令牌，失败时回滚实体上的版本值；
        // 受影响 0 行说明库中版本已被他人推进，抛并发冲突业务异常。
        // 更新人/更新时间仍由 Aop.DataExecuting 自动填充。
        // 注意：SqlSugar 的 ExecuteCommandWithOptLockAsync 暂无 CancellationToken 重载，取消令牌不向下传递。
        if (Client.Ado.Transaction is not null)
        {
            // 已处于业务事务（UnitOfWork / TransactionBehavior）内：直接执行，随外层事务统一提交或回滚
            var rows = await BuildUpdate(entity)
                .ExecuteCommandWithOptLockAsync();
            if (rows == 0)
            {
                throw new BusinessException(
                    ErrorCodes.CommonConcurrencyConflict,
                    "数据已被他人修改（版本号不一致），请刷新后重试。");
            }

            if (changeLogs.Count > 0)
            {
                await Client.Insertable(changeLogs).ExecuteCommandAsync(cancellationToken);
            }
        }
        else
        {
            // 无外层事务（系统任务等直写路径）：更新与变更日志在同一自持事务内原子完成（P1-R8），
            // 消除"更新成功但变更日志写入失败"产生无审计变更的窗口
            var result = await Client.Ado.UseTranAsync(async () =>
            {
                var rows = await BuildUpdate(entity)
                    .ExecuteCommandWithOptLockAsync();
                if (rows == 0)
                {
                    throw new BusinessException(
                        ErrorCodes.CommonConcurrencyConflict,
                        "数据已被他人修改（版本号不一致），请刷新后重试。");
                }

                if (changeLogs.Count > 0)
                {
                    await Client.Insertable(changeLogs).ExecuteCommandAsync();
                }
            });

            if (!result.IsSuccess)
            {
                throw result.ErrorException
                    ?? new BusinessException(ErrorCodes.CommonConcurrencyConflict, "数据更新失败，请重试。");
            }
        }

        return true;
    }

    /// <inheritdoc />
    public virtual async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await SoftDeleteAsync(it => it.Id == id, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual async Task<bool> SoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await SoftDeleteAsync(it => ids.Contains(it.Id), cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual async Task<bool> SoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await ExecDeleteOrRestore(false, predicate, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await RestoreAsync(it => it.Id == id, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await RestoreAsync(it => ids.Contains(it.Id), cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => await ExecDeleteOrRestore(true, predicate, cancellationToken, expectedVersion);

    /// <summary>
    /// 软删除 / 解除墓碑的共享核心：单条 UPDATE 按列翻转 <see cref="AggregateBase.IsDeleted"/>，
    /// 同时轮换 <see cref="AggregateBase.Version"/> 并兜底填充更新审计（更新时间总是填充；
    /// 更新人为当前登录用户，无登录上下文时为 null——按列更新绕过 Aop.DataExecuting，审计需在此显式补齐）。
    /// 条件追加与当前状态一致的比对保证幂等（重复执行返回 false）。
    /// 注意：传入 expectedVersion 时按列更新同样追加 Version CAS 条件，命中 0 行即并发冲突；
    /// 版本轮换使旧令牌持有的实体更新失效——防止软删除后墓碑被旧快照的 OptLock 更新复活。
    /// </summary>
    /// <param name="deleted">行的当前状态：true 表示已删除（执行恢复，置 0），false 表示未删除（执行软删除，置 1）。</param>
    /// <param name="predicate">筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件。</param>
    /// <returns>受影响行数 &gt; 0 返回 true；传入 expectedVersion 且命中 0 行时抛并发冲突，否则幂等返回 false。</returns>
    protected virtual async Task<bool> ExecDeleteOrRestore(bool deleted, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
    {
        var now = DateTime.UtcNow;
        var targetDeleted = !deleted;
        var userId = CurrentUser.UserId;
        var newVersion = Guid.NewGuid();
        // 软删除 / 恢复本质是按列更新，直接复用更新构建器（聚合根不提供物理删除语义）
        var update = BuildUpdate()
            .SetColumns(it => new TEntity
            {
                IsDeleted = targetDeleted,
                Version = newVersion,
                UpdatedAt = now,
                UpdatedBy = userId
            })
            .Where(predicate)
            .Where(it => it.IsDeleted == deleted);

        // 写入侧显式补租户条件：批量软删 / 恢复不得越界修改其他租户的行；
        // 平台上下文（null）保持全量，与全局查询过滤器口径一致。
        if (CurrentTenantId is { } tenantId)
        {
            update = update.Where(it => it.TenantId == tenantId);
        }

        // 命令携带的乐观锁版本令牌：按列更新也追加 Version CAS 条件，
        // 命中 0 行说明版本陈旧 / 目标不可见，按并发冲突拒绝（fail-closed，不静默吞掉）。
        if (expectedVersion is { } version)
        {
            update = update.Where(it => it.Version == version);
        }

        // 字段级变更日志：按与 UPDATE 相同的可见范围先取受影响 Id
        // （恢复场景的目标为墓碑，须关闭全局软删除 / 租户过滤器后显式补回租户条件）
        var targetIds = ChangeLogEnabled
            ? await QueryTargetIdsAsync(deleted, predicate, expectedVersion, cancellationToken)
            : [];

        var rows = await update.ExecuteCommandAsync(cancellationToken);
        if (rows == 0 && expectedVersion is not null)
        {
            throw new BusinessException(
                ErrorCodes.CommonConcurrencyConflict,
                "数据已被他人修改（版本号不一致或目标不可见），请刷新后重试。");
        }

        if (rows > 0 && targetIds.Count > 0)
        {
            var logs = targetIds
                .Select(id => EntityChangeLogger.BuildLifecycleChange(
                    typeof(TEntity), id, targetDeleted, now, userId))
                .ToList();
            await Client.Insertable(logs).ExecuteCommandAsync(cancellationToken);
        }

        return rows > 0;
    }

    /// <summary>按软删除 / 恢复的实际可见范围查询受影响实体 Id（与 UPDATE 条件对齐）。</summary>
    /// <param name="deleted">行的当前软删除状态。</param>
    /// <param name="predicate">业务筛选条件。</param>
    /// <param name="expectedVersion">乐观锁版本令牌（非 null 时同样追加到查询）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目标 Id 列表。</returns>
    private async Task<List<Guid>> QueryTargetIdsAsync(
        bool deleted,
        Expression<Func<TEntity, bool>> predicate,
        Guid? expectedVersion,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery()
            .ClearFilter<TEntity>()
            .Where(predicate)
            .Where(it => it.IsDeleted == deleted);

        if (CurrentTenantId is { } tenantId)
        {
            query = query.Where(it => it.TenantId == tenantId);
        }

        if (expectedVersion is { } version)
        {
            query = query.Where(it => it.Version == version);
        }

        return await query.Select(it => it.Id).ToListAsync(cancellationToken);
    }
}
