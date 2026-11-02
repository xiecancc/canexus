using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Tenants;
/// <summary>租户分页查询请求（GET /api/system/tenant/page）。</summary>
public class TenantPageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配租户名称 / 租户标识。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>租户列表行（分页数据项）。</summary>
public class TenantPagedResponse : AggregatePagedResponse
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>租户标识（即请求头 X-Tenant-Id 取值）。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    public string? ContactPhone { get; set; }

    /// <summary>过期时间（可空）。</summary>
    public DateTime? ExpireTime { get; set; }
}

/// <summary>租户下拉查询请求：租户下拉查询载荷（预留为后续筛选条件的挂载点）。</summary>
public class TenantDropdownRequest : AggregateDropdownRequest
{
}

/// <summary>租户下拉简项（供用户创建时选择所属租户）。</summary>
public class TenantDropdownResponse : AggregateDropdownResponse
{
    /// <summary>租户标识。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;
}

/// <summary>租户写入请求（POST / PUT 共用载荷；镜像 SysTenant 聚合的启停 / 备注与业务字段；TenantKey 创建后不可变更）。</summary>
public class TenantInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>租户标识（唯一，即请求头 X-Tenant-Id 的取值）。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    public string? ContactPhone { get; set; }

    /// <summary>过期时间（可空，不强制校验）。</summary>
    public DateTime? ExpireTime { get; set; }
}


