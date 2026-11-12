
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.DataAccess;
using Infrastructure.Shared.Tenants;
using MySqlConnector;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar 的聚合仓储实现（对应 <see cref="AggregateBase"/>）：
/// 继承领域仓储（查询 / 插入 / 物理删除），补充乐观并发更新、软删除与恢复能力。
/// <para><b>乐观并发全覆盖</b>：更新、软删除、恢复三条写入路径统一经
/// <c>ExecuteCommandWithOptLockAsync</c> 生成 <c>UPDATE ... WHERE Version = 传入值</c>，
/// 命中 0 行即抛并发冲突业务异常；成功后由框架自动轮换版本令牌。
/// 软删除 / 恢复按列只更新 <see cref="AggregateBase.DeletedAt"/>、唯一键槽
/// <see cref="AggregateBase.DeletedKey"/>、更新审计与版本列，
/// 版本轮换使旧快照的更新失效，杜绝「墓碑被旧快照复活」。
/// 命令携带版本令牌时以其作为 CAS 条件（陈旧令牌直接冲突），未携带时取库中当前版本。</para>
/// <para>软删除以可空时间戳 <see cref="AggregateBase.DeletedAt"/> 为墓碑：null 表示未删除，
/// 非 null 表示删除发生时间；恢复即重新置为 null。全局查询过滤器按同一口径过滤，
/// 恢复类操作需先 <c>ClearFilter</c> 读取墓碑，并显式补回租户条件。</para>
/// <para>删除通道显式分离：派生仓储默认调用软删除 <c>SoftDeleteAsync</c>；
/// 基类继承的 <see cref="DomainRepository{TEntity}.HardDeleteAsync"/> 仍是物理删除
/// （单条 DELETE，不可恢复），仅在确需彻底清除数据时显式调用。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文（软删除 / 恢复的更新审计人取值来源）。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class AggregateRepository<TEntity>(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext)
    : DomainRepository<TEntity>(client, tenantContext), IAggregateRepository<TEntity>
    where TEntity : AggregateBase, new()
{
    /// <summary>当前用户上下文（更新审计人取值来源；无登录上下文时为 null，可空语义下不记录操作人）。</summary>
    protected IUserContext CurrentUser => currentUser;

    /// <summary>是否记录字段级变更日志（新增 / 更新 / 软删 / 恢复均写 sys_entity_change_log）；
    /// 高频自写表（如刷新令牌轮换）可重写为 false 关闭。</summary>
    protected virtual bool EntityChangeLogEnabled => true;

    /// <summary>变更日志开关（含自引用防护）：日志实体自身不得再产生变更日志，避免无限递归。</summary>
    private bool ChangeLogEnabled => EntityChangeLogEnabled && typeof(TEntity) != typeof(SysEntityChangeLog);

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 清除软删除 / 租户全局过滤器后按主键取墓碑，再按请求租户上下文（含平台用户通过
        // X-Tenant-Id 指定的租户）显式约束，避免恢复前校验因默认查询过滤而读不到目标行，
        // 同时不放大跨租户读取范围（口径与 LoadTargetsAsync 一致）。
        var query = BuildQuery()
            .ClearFilter<TEntity>()
            .Where(it => it.Id == id && it.DeletedAt != null);

        if (CurrentTenantId is { } tenantId)
        {
            query = query.Where(it => it.TenantId == tenantId);
        }

        return await query.FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>插入成功后按字段补一条「新增」变更日志（OldValue 为 null，敏感字段值脱敏）；
    /// 与业务插入同连接，处于外层事务时随其原子提交。
    /// 数据库唯一索引冲突（并发创建同键数据的应用层查重竞态窗口）转业务错误码返回。</remarks>
    public override async Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        bool inserted;
        try
        {
            inserted = await base.InsertAsync(entity, cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            throw UniqueConflict();
        }

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
    /// <remarks>批量插入（如用户导入）同样逐实体补字段级「新增」变更日志。
    /// 数据库唯一索引冲突（并发导入同键数据）转业务错误码返回。</remarks>
    public override async Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var materialized = entities as List<TEntity> ?? [.. entities];
        bool inserted;
        try
        {
            inserted = await base.InsertAsync(materialized, cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            throw UniqueConflict();
        }
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
                if (IsUniqueViolation(result.ErrorException))
                {
                    throw UniqueConflict();
                }

                throw result.ErrorException
                    ?? new BusinessException(ErrorCodes.CommonConcurrencyConflict, "数据更新失败，请重试。");
            }
        }

        return true;
    }

    /// <inheritdoc />
    public virtual Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => SoftDeleteAsync(it => it.Id == id, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual Task<bool> SoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => SoftDeleteAsync(it => ids.Contains(it.Id), cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual Task<bool> SoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => ExecDeleteOrRestoreAsync(false, predicate, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => RestoreAsync(it => it.Id == id, cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => RestoreAsync(it => ids.Contains(it.Id), cancellationToken, expectedVersion);

    /// <inheritdoc />
    public virtual Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
        => ExecDeleteOrRestoreAsync(true, predicate, cancellationToken, expectedVersion);

    /// <summary>
    /// 软删除 / 恢复的共享核心：按当前状态筛出目标行，逐行经 <c>ExecuteCommandWithOptLockAsync</c>
    /// 按列更新 <see cref="AggregateBase.DeletedAt"/>、更新审计与版本列。
    /// <para>三条写入路径（更新 / 软删除 / 恢复）统一使用同一套框架级乐观锁：
    /// 命令携带 expectedVersion 时以其作为 CAS 条件，陈旧令牌命中 0 行即抛并发冲突；
    /// 未携带时取库中当前版本，仍由框架校验并自动轮换版本令牌。
    /// 批量路径在无外层事务时自持事务，保证多行写入的原子性。</para>
    /// </summary>
    /// <param name="deleted">目标行的当前状态：true 表示墓碑（执行恢复，置 null），false 表示未删除（执行软删除，写入时间戳）。</param>
    /// <param name="predicate">筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时作为 CAS 条件。</param>
    /// <returns>命中并更新返回 true；无匹配且未携带版本令牌时幂等返回 false。</returns>
    /// <exception cref="BusinessException">版本令牌陈旧或目标不可见（命中 0 行）时抛出并发冲突。</exception>
    protected virtual async Task<bool> ExecDeleteOrRestoreAsync(
        bool deleted,
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default,
        Guid? expectedVersion = null)
    {
        var targets = await LoadTargetsAsync(deleted, predicate, cancellationToken);
        if (targets.Count == 0)
        {
            if (expectedVersion is not null)
            {
                throw ConcurrencyConflict();
            }

            return false;
        }

        var now = DateTime.UtcNow;
        var userId = CurrentUser.UserId;

        async Task ApplyAsync()
        {
            var logs = new List<SysEntityChangeLog>(targets.Count);
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var oldDeletedAt = target.DeletedAt;
                var newDeletedAt = deleted ? (DateTime?)null : now;

                // 版本令牌：命令携带时以客户端值参与 CAS（陈旧令牌必然命中 0 行），
                // 否则用刚读出的库中版本；ExecuteCommandWithOptLock 成功后自动轮换为新 Guid。
                target.Version = expectedVersion ?? target.Version;
                target.DeletedAt = newDeletedAt;
                target.DeletedKey = deleted ? string.Empty : DeleteMarker(target.Id);
                target.UpdatedAt = now;
                target.UpdatedBy = userId;

                // 只更新墓碑、唯一键槽、更新审计与版本列，不触碰任何业务字段；
                // Version 必须出现在 UpdateColumns 中，框架才会在 SET 子句里轮换版本令牌。
                var rows = await BuildUpdate(target)
                    .UpdateColumns(it => new { it.DeletedAt, it.DeletedKey, it.UpdatedAt, it.UpdatedBy, it.Version })
                    .ExecuteCommandWithOptLockAsync();
                if (rows == 0)
                {
                    throw ConcurrencyConflict();
                }

                if (ChangeLogEnabled)
                {
                    logs.Add(EntityChangeLogger.BuildLifecycleChange(
                        typeof(TEntity), target.Id, oldDeletedAt, newDeletedAt, now, userId));
                }
            }

            if (logs.Count > 0)
            {
                await Client.Insertable(logs).ExecuteCommandAsync(cancellationToken);
            }
        }

        if (Client.Ado.Transaction is not null)
        {
            // 已处于业务事务：随外层事务统一提交或回滚
            await ApplyAsync();
        }
        else
        {
            var result = await Client.Ado.UseTranAsync(ApplyAsync);
            if (!result.IsSuccess)
            {
                if (IsUniqueViolation(result.ErrorException))
                {
                    throw UniqueConflict();
                }

                throw result.ErrorException ?? ConcurrencyConflict();
            }
        }

        return true;
    }

    /// <summary>
    /// 按软删除 / 恢复的实际可见范围读取目标实体（与写入条件对齐）。
    /// <para>恢复场景目标是墓碑，须先清除全局软删除 / 租户过滤器，再显式补回租户条件，
    /// 避免跨租户恢复；平台上下文（null）保持全量，与全局查询过滤器口径一致。</para>
    /// </summary>
    /// <param name="deleted">目标行的当前软删除状态。</param>
    /// <param name="predicate">业务筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目标实体列表。</returns>
    private async Task<List<TEntity>> LoadTargetsAsync(
        bool deleted,
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery()
            .ClearFilter<TEntity>()
            .Where(predicate)
            .Where(StateFilter(deleted));

        // 写入侧显式补租户条件：批量软删 / 恢复不得越界修改其他租户的行；
        // 平台上下文（null）保持全量，与全局查询过滤器口径一致。
        if (CurrentTenantId is { } tenantId)
        {
            query = query.Where(it => it.TenantId == tenantId);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 按软删除状态生成过滤表达式。
    /// <para>分支在 C# 侧求值后再返回表达式树：若把三元写进 lambda 内部，SqlSugar 会翻译成
    /// 不受支持的 <c>SqlFunc.IIF</c> 并抛 <c>SqlSugarException</c>。</para>
    /// </summary>
    /// <param name="deleted">true 取墓碑（DeletedAt 非 null），false 取未删除（DeletedAt 为 null）。</param>
    /// <returns>状态过滤表达式。</returns>
    private static Expression<Func<TEntity, bool>> StateFilter(bool deleted)
    {
        return deleted
            ? (Expression<Func<TEntity, bool>>)(it => it.DeletedAt != null)
            : it => it.DeletedAt == null;
    }

    /// <summary>构造统一的乐观并发冲突业务异常（错误码 CommonConcurrencyConflict）。</summary>
    /// <returns>业务异常实例。</returns>
    private static BusinessException ConcurrencyConflict()
    {
        return new BusinessException(
            ErrorCodes.CommonConcurrencyConflict,
            "数据已被他人修改（版本号不一致或目标不可见），请刷新后重试。");
    }

    /// <summary>MySQL 唯一索引冲突错误码（ER_DUP_ENTRY，文档号 1062）。</summary>
    private const int MySqlDuplicateEntryNumber = 1062;

    /// <summary>判断异常链中是否存在 MySQL 唯一索引冲突（含 SqlSugar 包装的内部异常）。</summary>
    /// <param name="exception">待检查异常（可为 null）。</param>
    /// <returns>存在唯一冲突时返回 true。</returns>
    private static bool IsUniqueViolation(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: MySqlDuplicateEntryNumber })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>构造数据库唯一索引冲突业务异常（错误码 CommonDataDuplicate）。
    /// <para>这是应用层查重（EnsureNotDuplicateAsync）的并发竞态兜底：先查后插窗口内的并发创建
    /// 由数据库唯一索引拦截，业务语义仍归「数据重复」而非 500 系统错误。</para></summary>
    /// <returns>业务异常实例。</returns>
    private static BusinessException UniqueConflict()
    {
        return new BusinessException(
            ErrorCodes.CommonDataDuplicate,
            "数据唯一性冲突：同键数据已存在（可能是并发创建），请刷新后重试。");
    }

    /// <summary>生成软删除墓碑的唯一键槽标记（"#" + 主键 N 格式，32 位十六进制）。</summary>
    /// <param name="id">聚合主键。</param>
    /// <returns>墓碑键槽值；与未删除行恒空串的槽位约定配对。</returns>
    private static string DeleteMarker(Guid id) => $"#{id:N}";
}
