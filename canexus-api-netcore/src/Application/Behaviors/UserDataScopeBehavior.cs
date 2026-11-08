using Application.Contracts.Abstractions;
using Application.Contracts.Commands;
using Application.Scopes;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using MediatR;

namespace Application.Behaviors;
/// <summary>
/// 用户写命令数据范围守卫（S2）：在处理器执行前按当前用户数据范围校验目标用户是否可写。
/// <para>命中 <see cref="IUserDataScopeCommand"/> 的命令（更新 / 删除 / 恢复 / 改状态 / 重置密码 / 分配角色）
/// 统一在此兜底，处理器无需逐处重复；越界由 <see cref="IDataScopeFilter.EnsureInScopeAsync"/> 抛
/// AuthForbidden（HTTP 403）。超管（All）与数据范围可覆盖目标部门 / 本人的角色保持原有行为。</para>
/// <para>恢复命令的目标处于软删除墓碑状态，普通查询不可见，改用 <see cref="IAggregateRepository{TEntity}.GetDeletedAsync"/>
/// 读取墓碑后校验，避免"恢复"成为绕过数据范围的入口。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="dataScopeFilter">数据范围过滤器。</param>
/// <param name="userRepository">用户聚合仓储。</param>
public sealed class UserDataScopeBehavior<TRequest, TResponse>(
    IDataScopeFilter dataScopeFilter,
    IAggregateRepository<SysUser> userRepository) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is not IUserDataScopeCommand command)
        {
            return await next();
        }

        // fail-closed：目标不可见（不存在 / 被全局过滤器排除 / 已软删）时不得放行到处理器，
        // 避免「查不到就不校验」成为绕过数据范围的入口
        var user = (request is UserRestoreCommand
                ? await userRepository.GetDeletedAsync(command.TargetUserId, cancellationToken)
                : await userRepository.GetAsync([it => it.Id == command.TargetUserId], cancellationToken))
            ?? throw new BusinessException(ErrorCodes.AuthForbidden, "目标用户不存在或不在可见范围内，已拒绝访问");

        await dataScopeFilter.EnsureInScopeAsync(user.DeptId, user.Id, user.CreatedBy, cancellationToken);
        return await next();
    }
}
