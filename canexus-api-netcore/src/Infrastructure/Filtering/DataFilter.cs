
using Infrastructure.Shared.Filtering;

namespace Infrastructure.Filtering;
/// <summary>
/// 数据过滤开关实现：软删除与租户过滤默认启用。
/// <para>本服务以单例注册，但状态存于 <see cref="AsyncLocal{T}"/>：关闭过滤只影响当前异步上下文（请求），
/// 不会像普通实例字段那样在并发请求之间污染全局可见性。未显式设置时回落到默认 true。</para>
/// </summary>
public sealed class DataFilter : IDataFilter
{
    private static readonly AsyncLocal<bool?> SoftDeleteOverride = new();
    private static readonly AsyncLocal<bool?> TenantFilterOverride = new();

    /// <inheritdoc />
    public bool SoftDeleteEnabled
    {
        get => SoftDeleteOverride.Value ?? true;
        set => SoftDeleteOverride.Value = value;
    }

    /// <inheritdoc />
    public bool TenantFilterEnabled
    {
        get => TenantFilterOverride.Value ?? true;
        set => TenantFilterOverride.Value = value;
    }
}
