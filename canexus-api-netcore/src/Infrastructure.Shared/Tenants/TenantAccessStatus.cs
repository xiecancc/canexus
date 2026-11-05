namespace Infrastructure.Shared.Tenants;

/// <summary>租户访问校验结果：标识请求头指定的租户是否可用（存在、启用且未到期）。</summary>
public enum TenantAccessStatus
{
    /// <summary>租户存在、启用且未到期，允许访问。</summary>
    Ok,

    /// <summary>租户不存在（可能已被删除）。</summary>
    NotFound,

    /// <summary>租户已停用（Status=Disabled）。</summary>
    Disabled,

    /// <summary>租户已到期（ExpireTime 早于当前时间）。</summary>
    Expired,
}
