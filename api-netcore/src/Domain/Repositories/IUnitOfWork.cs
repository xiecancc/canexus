namespace Domain.Repositories;

/// <summary>
/// 工作单元接口：统一管理数据库事务边界。
/// <para>仅暴露「在事务中运行」语义：委托体内的全部仓储变更于同一事务执行，
/// 成功自动提交；抛出异常自动回滚并原样透出，无需显式开启 / 提交 / 回滚。</para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>在数据库事务中执行委托：成功自动提交，异常自动回滚并原样抛出。</summary>
    /// <typeparam name="T">委托返回值类型。</typeparam>
    /// <param name="action">事务内执行的委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>委托返回值。</returns>
    Task<T> RunInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>在数据库事务中执行委托（无返回值重载）。</summary>
    /// <param name="action">事务内执行的委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RunInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
