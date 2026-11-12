
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Online;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;

namespace Application.Queries;
/// <summary>分页查询在线会话处理器：活跃 refreshToken 记录即在线会话，批量组装用户与部门展示信息。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="refreshTokenRepository">刷新令牌仓储（活跃会话数据源）。</param>
/// <param name="userRepository">用户仓储（展示信息组装）。</param>
/// <param name="deptRepository">部门仓储（部门名称组装）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class OnlineUserQueryPagedHandler(
    ISysRefreshTokenRepository refreshTokenRepository,
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysDept> deptRepository,
    IMapper mapper) : QueryHandler<OnlineUserQueryPaged, PagedResponse<OnlineUserPagedResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<OnlineUserPagedResponse>> HandleAsync(OnlineUserQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var userName = NormalizeKeyword(request.UserName);
        var ip = NormalizeKeyword(request.Ip);
        var now = DateTime.UtcNow;

        // 用户名过滤：refreshToken 表不冗余账号，先解析匹配用户 Id 集合（空集合直接短路返回空页）
        List<Guid>? userIds = null;
        if (userName is not null)
        {
            userIds = [.. (await userRepository.GetListAsync([u => u.UserName.Contains(userName)], cancellationToken: cancellationToken)).Select(u => u.Id)];
            if (userIds.Count == 0)
            {
                return PagedResponse<OnlineUserPagedResponse>.Create([], 0, request);
            }
        }

        Expression<Func<SysRefreshToken, bool>> predicate = t =>
            (ip == null || (t.Ip != null && t.Ip.Contains(ip)))
            && (userIds == null || userIds.Contains(t.UserId));

        var page = await refreshTokenRepository.GetPagedActiveAsync(request, now, predicate, cancellationToken);
        var tokens = page.Items.ToList();

        // 页内批量组装用户（账号 / 昵称）与部门名称，避免逐行查询
        var items = mapper.Map<List<OnlineUserPagedResponse>>(tokens);
        var pageUserIds = tokens.Select(t => t.UserId).Distinct().ToList();
        var users = (await userRepository.GetListAsync([u => pageUserIds.Contains(u.Id)], cancellationToken: cancellationToken))
            .ToDictionary(u => u.Id);
        var deptIds = users.Values
            .Where(u => u.DeptId.HasValue)
            .Select(u => u.DeptId!.Value)
            .Distinct()
            .ToList();
        var depts = deptIds.Count == 0
            ? []
            : (await deptRepository.GetListAsync([d => deptIds.Contains(d.Id)], cancellationToken: cancellationToken))
                .ToDictionary(d => d.Id, d => d.DeptName);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var item = items[i];
            item.ExpiresAt = token.ExpiresAt;
            if (users.TryGetValue(token.UserId, out var user))
            {
                item.UserName = user.UserName;
                item.NickName = user.NickName;
                if (user.DeptId.HasValue && depts.TryGetValue(user.DeptId.Value, out var deptName))
                {
                    item.DeptName = deptName;
                }
            }
        }

        return PagedResponse<OnlineUserPagedResponse>.Create(items, page.Total, request);
    }
}
