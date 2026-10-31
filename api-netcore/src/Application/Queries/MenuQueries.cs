
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取菜单详情查询处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetMenuDetailQueryHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper) : QueryHandler<GetMenuDetailQuery, MenuDetailVO>
{
    /// <inheritdoc />
    protected override async Task<MenuDetailVO> HandleAsync(GetMenuDetailQuery query, CancellationToken cancellationToken)
    {
        var menu = await menuRepository.GetAsync([it => it.Id == query.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {query.Id} 不存在");
        return mapper.Map<MenuDetailVO>(menu);
    }
}

/// <summary>查询菜单树处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetMenuTreeQueryHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper) : QueryHandler<GetMenuTreeQuery, List<MenuTreeVO>>
{
    /// <inheritdoc />
    protected override async Task<List<MenuTreeVO>> HandleAsync(GetMenuTreeQuery query, CancellationToken cancellationToken)
    {
        var menus = await menuRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = menus.Where(m =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || m.MenuName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (m.Perms is not null && m.Perms.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status is null || m.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿菜单挂到根级不丢失），实体树递归映射为 VO 树
        return mapper.Map<List<MenuTreeVO>>(SysMenu.BuildTree(filtered));
    }
}
