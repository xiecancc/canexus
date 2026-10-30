namespace Application.Queries;

using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Tenants;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

/// <summary>查询启用租户简易列表处理器。</summary>
public sealed class GetAllTenantsQueryHandler : QueryHandler<GetAllTenantsQuery, List<TenantSimpleVO>>
{
    private readonly IAggregateRepository<SysTenant> _tenantRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="tenantRepository">租户仓储。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetAllTenantsQueryHandler(IAggregateRepository<SysTenant> tenantRepository, IMapper mapper)
    {
        _tenantRepository = tenantRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<List<TenantSimpleVO>> HandleAsync(GetAllTenantsQuery query, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetListAsync([t => t.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        return tenants
            .OrderBy(t => t.Id)
            .Select(t => _mapper.Map<TenantSimpleVO>(t))
            .ToList();
    }
}

/// <summary>分页查询租户处理器。</summary>
public sealed class GetTenantPageQueryHandler : QueryHandler<GetTenantPageQuery, PagedResult<TenantListVO>>
{
    private readonly IDomainRepository<SysTenant> _tenantRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="tenantRepository">租户仓储（只读）。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetTenantPageQueryHandler(IDomainRepository<SysTenant> tenantRepository, IMapper mapper)
    {
        _tenantRepository = tenantRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<TenantListVO>> HandleAsync(GetTenantPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysTenant, bool>> predicate = t =>
            (keyword == null || t.TenantName.Contains(keyword) || t.TenantKey.Contains(keyword))
            && (request.Status == null || t.Status == request.Status.Value);

        var page = await _tenantRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(t => _mapper.Map<TenantListVO>(t)).ToList();
        return PagedResult<TenantListVO>.Create(items, page.Total, request);
    }
}
