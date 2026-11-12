
using Infrastructure.Scheduling;

namespace Infrastructure;

/// <summary>
/// 平台级跨租户全表操作白名单（P1-2）：集中登记「刻意不补租户条件、对全表生效」的写入点，
/// 供代码评审与安全审计核对。列入本白名单仅表示「该处已知且刻意不加租户过滤」，
/// 不代表业务语义已确认无误——各项理由均标注「业务语义待核实」，待业务复核后再决定是否收敛。
/// <para>白名单之外的任何全表写入都应视为「缺少租户隔离」的潜在缺陷，不得默认放行；
/// 新增平台级全表操作必须同时在此登记并在调用点引用，以保持清单与代码一致。</para>
/// </summary>
public static class PlatformWideOperationWhitelist
{
    /// <summary>日志保留清理作业（按稳定 JobKey 关联 <c>LogRetentionJob</c>）。</summary>
    public const string LogRetention = JobTypeResolver.LogRetentionJobKey;

    /// <summary>过期刷新令牌回收作业（按稳定 JobKey 关联 <c>RefreshTokenCleanupJob</c>）。</summary>
    public const string RefreshTokenCleanup = JobTypeResolver.RefreshTokenCleanupJobKey;

    /// <summary>用户令牌版本批量递增（关联 <c>TokenVersionService.RevokeAllForUsersAsync</c> 的独立连接全表 UPDATE）。</summary>
    public const string TokenVersionRevokeAll = "token-version:revoke-all";

    /// <summary>
    /// 全部已登记操作：操作标识 → 允许跨租户全表操作的原因（业务语义待核实）。
    /// </summary>
    /// <returns>只读登记表；键为操作标识，值为允许原因。键集合即允许清单，须与调用点逐一对应。</returns>
    public static IReadOnlyDictionary<string, string> Operations { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LogRetention] = "日志保留是平台统一保留策略，按时间裁剪四类日志表的过期行，对全部租户生效（业务语义待核实）。",
            [RefreshTokenCleanup] = "过期刷新令牌回收是平台级会话清理，按过期时间回收全表行，保留宽限期内吊销证据（业务语义待核实）。",
            [TokenVersionRevokeAll] = "令牌版本按用户 Id 精确递增，属平台级会话吊销，CopyNew 独立连接不补租户条件（业务语义待核实）。"
        };
}
