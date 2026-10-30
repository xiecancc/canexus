namespace Infrastructure.Filtering;

using Infrastructure.Shared.Filtering;

/// <summary>数据过滤开关实现：软删除与租户过滤默认启用。</summary>
public sealed class DataFilter : IDataFilter
{
    /// <inheritdoc />
    public bool SoftDeleteEnabled { get; set; } = true;

    /// <inheritdoc />
    public bool TenantFilterEnabled { get; set; } = true;
}
