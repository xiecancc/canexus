namespace Infrastructure.Shared.Filtering;

/// <summary>
/// 数据过滤开关：从简实现，控制软删除过滤与租户过滤是否生效（默认全部启用）。
/// <para>运行时切换开关后，下一次查询立即按新状态构建过滤条件。</para>
/// </summary>
public interface IDataFilter
{
    /// <summary>软删除过滤是否启用（对实现 <c>ISoftDelete</c> 的实体生效）。</summary>
    bool SoftDeleteEnabled { get; set; }

    /// <summary>租户过滤是否启用（对实现 <c>IMultiTenant</c> 的实体生效）。</summary>
    bool TenantFilterEnabled { get; set; }
}
