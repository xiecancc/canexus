namespace Infrastructure.Shared.Tenants;

/// <summary>
/// 当前租户上下文：保存请求级租户标识，默认 null 表示未指定租户（不启用租户过滤）。
/// <para>多租户完整实现（Header 解析、租户管理）在后续任务提供，这里仅保留过滤钩子。</para>
/// </summary>
public interface ITenantContext
{
    /// <summary>当前租户 Id；null 表示未指定（租户过滤条件不生效）。</summary>
    public Guid? TenantId { get; set; }
}
