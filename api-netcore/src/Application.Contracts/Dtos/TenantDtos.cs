
using Application.Contracts.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Tenants;
/// <summary>租户分页查询请求（GET /api/system/tenant/page）。</summary>
public class TenantPageRequest : PagedRequest
{
    /// <summary>关键字：模糊匹配租户名称 / 租户标识。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态：启用（1）/ 停用（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>租户列表行（分页数据项）。</summary>
public class TenantListVO : StatusVO
{
    /// <summary>租户标识（即请求头 X-Tenant-Id 取值）。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    public string? ContactPhone { get; set; }

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>过期时间（可空）。</summary>
    public DateTime? ExpireTime { get; set; }
}

/// <summary>租户下拉简项（供用户创建时选择所属租户）。</summary>
public class TenantSimpleVO
{
    /// <summary>租户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>租户标识。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;
}

/// <summary>租户写入请求基类：创建 / 更新共同字段（对齐 SysTenant 聚合；TenantKey 创建后不可变更）。</summary>
public abstract class TenantWriteRequest : StatusDescriptionRequest
{
    /// <summary>租户标识（唯一，即请求头 X-Tenant-Id 的取值）。</summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>租户名称。</summary>
    public string TenantName { get; set; } = string.Empty;

    /// <summary>联系电话。</summary>
    public string? ContactPhone { get; set; }

    /// <summary>过期时间（可空，不强制校验）。</summary>
    public DateTime? ExpireTime { get; set; }
}

/// <summary>创建租户请求（POST /api/system/tenant）。</summary>
public class TenantCreateRequest : TenantWriteRequest
{
}

/// <summary>更新租户请求（PUT /api/system/tenant/{id}；TenantKey 不可变更）。</summary>
public class TenantUpdateRequest : TenantWriteRequest
{
}
