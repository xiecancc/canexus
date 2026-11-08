namespace Infrastructure.Shared.Filtering;

/// <summary>
/// 数据过滤开关：从简实现，控制软删除过滤与租户过滤是否生效（默认全部启用）。
/// <para>运行时切换开关后，下一次查询立即按新状态构建过滤条件；开关状态按异步上下文隔离，
/// 一个请求关闭过滤不得影响并发的其他请求（含其他租户）。</para>
/// </summary>
public interface IDataFilter
{
    /// <summary>软删除过滤是否启用（对实现 <c>ISoftDelete</c> 的实体生效）。</summary>
    public bool SoftDeleteEnabled { get; set; }

    /// <summary>租户过滤是否启用（对实现 <c>IMultiTenant</c> 的实体生效）。</summary>
    public bool TenantFilterEnabled { get; set; }
}
