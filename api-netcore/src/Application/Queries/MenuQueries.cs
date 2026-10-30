namespace Application.Queries;

using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Shared.Enums;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>获取菜单详情查询处理器。</summary>
public sealed class GetMenuDetailQueryHandler : QueryHandler<GetMenuDetailQuery, MenuDetailVO>
{
    private readonly IAggregateTreeRepository<SysMenu> _menuRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetMenuDetailQueryHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper)
    {
        _menuRepository = menuRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<MenuDetailVO> HandleAsync(GetMenuDetailQuery query, CancellationToken cancellationToken)
    {
        var menu = await _menuRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {query.Id} 不存在");
        return _mapper.Map<MenuDetailVO>(menu);
    }
}

/// <summary>查询菜单树处理器。</summary>
public sealed class GetMenuTreeQueryHandler : QueryHandler<GetMenuTreeQuery, List<MenuTreeVO>>
{
    private readonly IAggregateTreeRepository<SysMenu> _menuRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetMenuTreeQueryHandler(IAggregateTreeRepository<SysMenu> menuRepository, IMapper mapper)
    {
        _menuRepository = menuRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<List<MenuTreeVO>> HandleAsync(GetMenuTreeQuery query, CancellationToken cancellationToken)
    {
        var menus = await _menuRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = menus.Where(m =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || m.MenuName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (m.Perms != null && m.Perms.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status == null || m.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿菜单挂到根级不丢失），实体树递归映射为 VO 树
        return _mapper.Map<List<MenuTreeVO>>(SysMenu.BuildTree(filtered));
    }
}
