
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Paging;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的登录日志仓储实现（清空与单条删除均为物理删除，支持按登录时间倒序分页）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
public class LoginLogRepository(ISqlSugarClient client, IUserContext currentUser) : AggregateRepository<SysLoginLog>(client, currentUser), ILoginLogRepository
{
    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Client.Deleteable<SysLoginLog>().ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await Client.Deleteable<SysLoginLog>().In(id).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResponse<SysLoginLog>> GetPagedByTimeDescAsync(
        PagedRequest request,
        Expression<Func<SysLoginLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = Client.Queryable<SysLoginLog>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var items = await query
            .OrderBy(it => it.LoginTime, OrderByType.Desc)
            .OrderBy(it => it.Id, OrderByType.Desc)
            .ToPageListAsync(request.PageIndex, request.PageSize, total);
        return PagedResponse<SysLoginLog>.Create(items, total.Value, request);
    }
}
