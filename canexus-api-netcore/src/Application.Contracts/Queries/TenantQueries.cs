
using Application.Contracts.Abstractions;
using Application.Contracts.Tenants;

namespace Application.Contracts.Queries;
/// <summary>查询启用状态的租户简易列表（下拉框等场景）。</summary>
public sealed record TenantQueryDropdown() : AggregateQueryDropdown<TenantDropdownRequest, TenantDropdownResponse>(new TenantDropdownRequest());

/// <summary>分页查询租户（keyword 模糊匹配租户名称 / 租户标识；status 精确）。</summary>
public sealed record TenantQueryPaged : AggregateQueryPaged<TenantPageRequest, TenantPagedResponse>
{
    /// <summary>构造租户分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public TenantQueryPaged(TenantPageRequest request) : base(request) { }
}
