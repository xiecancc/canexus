namespace Application.Queries;

using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;
using Domain.Shared.Enums;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Paging;
using Domain.Shared.Results;

/// <summary>获取全部启用角色查询处理器。</summary>
public sealed class GetAllEnabledRolesQueryHandler : QueryHandler<GetAllEnabledRolesQuery, List<RoleSimpleVO>>
{
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetAllEnabledRolesQueryHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper)
    {
        _roleRepository = roleRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<List<RoleSimpleVO>> HandleAsync(GetAllEnabledRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        return roles
            .OrderBy(r => r.Id)
            .Select(r => _mapper.Map<RoleSimpleVO>(r))
            .ToList();
    }
}

/// <summary>获取角色详情查询处理器。</summary>
public sealed class GetRoleDetailQueryHandler : QueryHandler<GetRoleDetailQuery, RoleDetailVO>
{
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IDomainRepository<SysRoleMenu> _roleMenuRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetRoleDetailQueryHandler(
        IAggregateRepository<SysRole> roleRepository,
        IDomainRepository<SysRoleMenu> roleMenuRepository,
        IMapper mapper)
    {
        _roleRepository = roleRepository;
        _roleMenuRepository = roleMenuRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<RoleDetailVO> HandleAsync(GetRoleDetailQuery query, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {query.Id} 不存在");
        var detail = _mapper.Map<RoleDetailVO>(role);
        detail.MenuIds = (await _roleMenuRepository.GetListAsync([rm => rm.RoleId == query.Id], cancellationToken: cancellationToken))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToList();
        return detail;
    }
}

/// <summary>分页查询角色处理器。</summary>
public sealed class GetRolePageQueryHandler : QueryHandler<GetRolePageQuery, PagedResult<RoleListVO>>
{
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetRolePageQueryHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper)
    {
        _roleRepository = roleRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<RoleListVO>> HandleAsync(GetRolePageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        var status = request.Status;
        Expression<Func<SysRole, bool>> predicate = r =>
            (keyword == null || r.RoleName.Contains(keyword) || r.RoleKey.Contains(keyword))
            && (status == null || r.Status == status.Value);

        var page = await _roleRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(role => _mapper.Map<RoleListVO>(role)).ToList();
        return PagedResult<RoleListVO>.Create(items, page.Total, request);
    }
}
