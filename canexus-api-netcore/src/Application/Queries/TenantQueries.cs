
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
/// <summary>查询启用租户简易列表处理器（标准下拉流程：仅启用项，按主键升序排序下推）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantRepository">租户仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class TenantQueryDropdownHandler(IAggregateRepository<SysTenant> tenantRepository, IMapper mapper) :
    AggregateQueryDropdownHandler<TenantQueryDropdown, SysTenant, TenantDropdownResponse>(tenantRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysTenant, bool>>? BuildDropdownPredicate() => t => t.Status == CommonStatus.Enabled;

    /// <inheritdoc />
    protected override IDictionary<Expression<Func<SysTenant, object>>, bool?>? BuildDropdownOrders() =>
        new Dictionary<Expression<Func<SysTenant, object>>, bool?> { [t => t.Id] = true };
}

/// <summary>分页查询租户处理器（标准分页流程：关键字模糊匹配租户名 / 租户标识、状态过滤）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantRepository">租户仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class TenantQueryPagedHandler(IAggregateRepository<SysTenant> tenantRepository, IMapper mapper) : AggregateQueryPagedHandler<TenantQueryPaged, TenantPageRequest, SysTenant, TenantListResponse>(tenantRepository, mapper)
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
