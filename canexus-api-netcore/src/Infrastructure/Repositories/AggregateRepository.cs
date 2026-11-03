
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
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
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文（软删除 / 恢复按列更新的更新审计人取值来源）。</param>
public class AggregateRepository<TEntity>(ISqlSugarClient client, IUserContext currentUser)
    : DomainRepository<TEntity>(client), IAggregateRepository<TEntity>
    where TEntity : AggregateBase, new()
{
    /// <summary>当前用户上下文（软删除 / 恢复按列更新的更新审计人取值来源；无登录上下文时为 null，
    /// 可空语义下不记录操作人）。</summary>
    protected IUserContext CurrentUser => currentUser;

    /// <inheritdoc />
    public virtual async Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 框架级乐观锁（Version 标记 IsEnableUpdateVersionValidation）：ExecuteCommandWithOptLock 自动生成
        // UPDATE ... WHERE Id = @id AND Version = 传入值，成功后自动轮换版本令牌，失败时回滚实体上的版本值；
        // 受影响 0 行说明库中版本已被他人推进，抛并发冲突业务异常。
        // 更新人/更新时间仍由 Aop.DataExecuting 自动填充。
        // 注意：SqlSugar 的 ExecuteCommandWithOptLockAsync 暂无 CancellationToken 重载，取消令牌不向下传递。
        var rows = await Client.Updateable(entity)
            .ExecuteCommandWithOptLockAsync();

        if (rows == 0)
        {
            throw new BusinessException(
                ErrorCodes.CommonConcurrencyConflict,
                "数据已被他人修改（版本号不一致），请刷新后重试。");
        }

        return true;
    }

    /// <inheritdoc />
    public virtual async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await DeleteAsync(it => it.Id == id, cancellationToken);

    /// <inheritdoc />
    public virtual async Task<bool> DeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => await DeleteAsync(it => ids.Contains(it.Id), cancellationToken);

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await ExecDeleteOrRestore(false, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        => await RestoreAsync(it => it.Id == id, cancellationToken);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => await RestoreAsync(it => ids.Contains(it.Id), cancellationToken);

    /// <inheritdoc />
    public virtual async Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await ExecDeleteOrRestore(true, predicate, cancellationToken);

    /// <summary>
    /// 软删除 / 解除墓碑的共享核心：单条 UPDATE 按列翻转 <see cref="AggregateBase.IsDeleted"/>，
    /// 同时轮换 <see cref="AggregateBase.Version"/> 并兜底填充更新审计（更新时间总是填充；
    /// 更新人为当前登录用户，无登录上下文时为 null——按列更新绕过 Aop.DataExecuting，审计需在此显式补齐）。
    /// 条件追加与当前状态一致的比对保证幂等（重复执行返回 false）。
    /// 注意：按列更新无逐行 CAS（拿不到每行旧版本），删除/恢复自身不检测并发冲突；
    /// 但版本轮换使旧令牌持有的实体更新失效——防止软删除后墓碑被旧快照的 OptLock 更新复活。
    /// </summary>
    /// <param name="deleted">行的当前状态：true 表示已删除（执行恢复，置 0），false 表示未删除（执行软删除，置 1）。</param>
    /// <param name="predicate">筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true（幂等重复执行或无匹配时为 false）。</returns>
    protected virtual async Task<bool> ExecDeleteOrRestore(bool deleted, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var targetDeleted = !deleted;
        var userId = CurrentUser.UserId;
        var newVersion = Guid.NewGuid();
        return await Client.Updateable<TEntity>()
            .SetColumns(it => new TEntity
            {
                IsDeleted = targetDeleted,
                Version = newVersion,
                UpdatedAt = now,
                UpdatedBy = userId
            })
            .Where(predicate)
            .Where(it => it.IsDeleted == deleted)
            .ExecuteCommandAsync(cancellationToken) > 0;
    }
}
