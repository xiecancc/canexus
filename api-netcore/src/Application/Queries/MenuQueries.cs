
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取菜单详情查询处理器（标准详情流程，无扩展字段组装）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class MenuQueryByIdHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper) : AggregateQueryByIdHandler<MenuQueryById, SysMenu, MenuDetailResponse>(menuRepository, mapper)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.MenuNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "菜单";
}

/// <summary>查询菜单树处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class MenuQueryTreeHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper) : QueryHandler<MenuQueryTree, List<MenuTreeResponse>>
{
    /// <inheritdoc />
    protected override async Task<List<MenuTreeResponse>> HandleAsync(MenuQueryTree query, CancellationToken cancellationToken)
    {
        var menus = await menuRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = menus.Where(m =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || m.MenuName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (m.Perms is not null && m.Perms.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status is null || m.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿菜单挂到根级不丢失），实体树递归映射为 Response 树
        return mapper.Map<List<MenuTreeResponse>>(SysMenu.BuildTree(filtered));
    }
}
