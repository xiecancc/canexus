
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar Ado 事务的工作单元实现（UseTranAsync）。
/// <para>开启事务 → 执行委托 → 成功提交 / 异常回滚；回滚后原样透出委托抛出的异常，
/// 保证上层错误码与语义不变。与仓储共用同一 SqlSugar 客户端，事务对全部仓储操作生效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（与仓储共用同一实例，事务对全部仓储操作生效）。</param>
public sealed class UnitOfWork(ISqlSugarClient client) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<T> RunInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunCoreAsync(action);
    }

    /// <inheritdoc />
    public Task RunInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        return RunInTransactionAsync<object?>(
            async () =>
            {
                await action();
                return null;
            },
            cancellationToken);
    }

    /// <summary>UseTranAsync：开启事务 → 执行委托 → 成功提交 / 异常回滚并捕获，统一在此原样透出。</summary>
    /// <typeparam name="T">委托返回值类型。</typeparam>
    /// <param name="action">事务内执行的委托。</param>
    /// <returns>委托返回值。</returns>
    private async Task<T> RunCoreAsync<T>(Func<Task<T>> action)
    {
        var result = await client.Ado.UseTranAsync(action);
        if (!result.IsSuccess)
        {
            throw result.ErrorException ?? new BusinessException(ErrorCodes.CommonOperationFailed, "事务执行失败，已回滚");
        }

        return result.Data;
    }
}
