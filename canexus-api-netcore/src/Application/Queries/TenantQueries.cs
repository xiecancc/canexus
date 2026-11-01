
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Tenants;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;

namespace Application.Queries;
/// <summary>查询启用租户简易列表处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantRepository">租户仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class TenantQueryListHandler(IAggregateRepository<SysTenant> tenantRepository, IMapper mapper) : QueryHandler<TenantQueryList, List<TenantSimpleResponse>>
{
    /// <inheritdoc />
    protected override async Task<List<TenantSimpleResponse>> HandleAsync(TenantQueryList query, CancellationToken cancellationToken)
    {
        var tenants = await tenantRepository.GetListAsync([t => t.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        var enabled = tenants.OrderBy(t => t.Id).ToList();
        return mapper.Map<List<TenantSimpleResponse>>(enabled);
    }
}

/// <summary>分页查询租户处理器（标准分页流程：关键字模糊匹配租户名 / 租户标识、状态过滤）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantRepository">租户仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class TenantQueryPagedHandler(IDomainRepository<SysTenant> tenantRepository, IMapper mapper) : DomainQueryPagedHandler<TenantQueryPaged, TenantPageRequest, SysTenant, TenantListResponse>(tenantRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysTenant, bool>>? BuildPredicate(TenantPageRequest request)
    {
        var keyword = NormalizeFilter(request.Keyword);
        return t =>
            (keyword == null || t.TenantName.Contains(keyword) || t.TenantKey.Contains(keyword))
            && (request.Status == null || t.Status == request.Status.Value);
    }
}
