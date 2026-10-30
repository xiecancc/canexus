namespace Infrastructure.Repositories;

using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using SqlSugar;

/// <summary>基于 SqlSugar 的登录日志仓储实现（清空与单条删除均为物理删除，支持按登录时间倒序分页）。</summary>
public class LoginLogRepository : AggregateRepository<SysLoginLog>, ILoginLogRepository
{
    /// <summary>构造函数。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    public LoginLogRepository(ISqlSugarClient client)
        : base(client)
    {
    }

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
    public async Task<PagedResult<SysLoginLog>> GetPagedByTimeDescAsync(
        PagedRequest request,
        Expression<Func<SysLoginLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = Client.Queryable<SysLoginLog>();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        var items = await query
            .OrderBy(it => it.LoginTime, OrderByType.Desc)
            .OrderBy(it => it.Id, OrderByType.Desc)
            .ToPageListAsync(request.PageIndex, request.PageSize, total);
        return PagedResult<SysLoginLog>.Create(items, total.Value, request);
    }
}
