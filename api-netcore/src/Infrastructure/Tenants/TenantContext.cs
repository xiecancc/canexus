namespace Infrastructure.Tenants;

using Infrastructure.Shared.Tenants;

/// <summary>
/// 基于 AsyncLocal 的租户上下文实现：以单例注册，异步上下文之间天然隔离，
/// 各请求设置互不干扰，默认 null（不启用租户过滤）。
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    /// <inheritdoc />
    public Guid? TenantId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
