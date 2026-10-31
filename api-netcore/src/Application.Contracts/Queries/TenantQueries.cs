
using Application.Contracts.Abstractions;
using Application.Contracts.Tenants;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>查询启用状态的租户简易列表（下拉框等场景）。</summary>
public sealed record GetAllTenantsQuery : IQuery<List<TenantSimpleVO>>;

/// <summary>分页查询租户（keyword 模糊匹配租户名称 / 租户标识；status 精确）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetTenantPageQuery(TenantPageRequest Request) : IQuery<PagedResult<TenantListVO>>;
