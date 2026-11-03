
using Application.Contracts.Abstractions;
using Domain.Repositories;
using MediatR;

namespace Application.Behaviors;
/// <summary>
/// 事务管道行为：对实现 <see cref="ICommand"/> 的写命令，在数据库事务中执行后续管道与处理器
/// （成功提交 / 异常自动回滚并原样透出），查询请求直接透放、不开启事务。
/// <para>事务边界覆盖「校验通过后的全部写入 + 领域事件发布」：处理器内不再手工管理
/// <see cref="IUnitOfWork"/>（历史上各处理器手工 <see cref="IUnitOfWork.RunInTransactionAsync{TResult}"/>，
/// 现统一收口到本行为，避免双开事务与遗漏事务两种问题）。注册顺序上位于 ValidationBehavior 之后
/// （最内层）：参数校验失败不开启事务；LoggingBehavior / PerformanceBehavior 的耗时统计包含事务开销。</para>
/// <para>领域事件监听器（缓存失效、调度重排等响应动作）随事务内发布执行：
/// 监听器做的是缓存 / 调度器等非库内副作用，提前于提交执行不会读到中间态；
/// 若未来出现必须提交后执行的动作（如 Outbox 投递），应挂接到提交后钩子而非本行为。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="unitOfWork">工作单元（与仓储共用同一 SqlSugar 客户端，事务对全部仓储操作生效）。</param>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // 非写命令（查询 / 其他请求）直接透放：只读路径零事务开销
        if (request is not ICommand)
        {
            return await next();
        }

        return await unitOfWork.RunInTransactionAsync(() => next(), cancellationToken);
    }
}
