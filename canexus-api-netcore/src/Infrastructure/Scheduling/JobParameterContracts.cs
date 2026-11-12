using Domain.Shared.Jobs;

namespace Infrastructure.Scheduling;

/// <summary>
/// 调度任务参数契约（P2-F2）：按白名单作业类型声明「该作业认识的参数键」及取值规则，
/// 入调度时对 sys_job 作业参数做结构化校验（未知键 / 值非法记 Warning，不阻断调度）。
/// <para>参数的结构合法性（JSON 对象、扁平标量、长度上限、保留键）已由
/// <see cref="JobParameters.Validate"/> 在命令验证器（JobValidators）与入调度
/// <see cref="JobParameters.Parse"/> fail-fast 双侧兜底；本契约补齐「键与取值是否为该作业所认识」
/// 的最后一层——误配参数（如给无参作业配了键、给保留作业配错值）在同步期即可发现，
/// 而不是等首次执行才暴露。Warning 而非 fail-fast：保持存量库向前兼容（旧配置多出的键
/// 不应阻塞应用启动），作业执行侧另有越界收敛兜底。</para>
/// </summary>
public static class JobParameterContracts
{
    /// <summary>作业参数契约：作业类型 → (已知参数键 → 单值校验，合法返回 null，非法返回错误说明)。</summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, Func<string, string?>>> Contracts =
        new(StringComparer.Ordinal)
        {
            [JobTypeResolver.LogRetentionJobKey] = new Dictionary<string, Func<string, string?>>
            {
                [LogRetentionJob.RetentionDaysParameter] = ValidateRetentionDays
            },

            // 无参作业：契约空表意味着任何参数键都视为未知键
            [JobTypeResolver.NoOpJobKey] = EmptyContract(),
            [JobTypeResolver.RefreshTokenCleanupJobKey] = EmptyContract()
        };

    /// <summary>校验作业参数是否符合该作业类型的参数契约。</summary>
    /// <param name="jobType">作业类型稳定标识（须已通过 <see cref="JobTypeResolver.IsAllowedInput"/>）。</param>
    /// <param name="parameters">已解析的扁平参数键值表（来自 <see cref="JobParameters.Parse"/>）。</param>
    /// <returns>问题清单（每项可直接展示）；参数全部合法时为空。</returns>
    public static IReadOnlyList<string> Validate(string jobType, IReadOnlyDictionary<string, string> parameters)
    {
        if (!Contracts.TryGetValue(jobType, out var contract))
        {
            return [];
        }

        var problems = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (!contract.TryGetValue(key, out var check))
            {
                problems.Add($"作业 {jobType} 不认识参数键「{key}」（已知键：{(contract.Count == 0 ? "无" : string.Join(" / ", contract.Keys))}）");
                continue;
            }

            var problem = check(value);
            if (problem is not null)
            {
                problems.Add($"参数「{key}」{problem}");
            }
        }

        return problems;
    }

    /// <summary>无参作业的空契约。</summary>
    /// <returns>空契约表。</returns>
    private static IReadOnlyDictionary<string, Func<string, string?>> EmptyContract() =>
        new Dictionary<string, Func<string, string?>>(StringComparer.Ordinal);

    /// <summary>保留天数取值校验：须为整数（合法区间外的值由作业执行侧收敛，此处仅拦截不可解析的值）。</summary>
    /// <param name="value">参数原始字符串值。</param>
    /// <returns>合法返回 null；非法返回错误说明。</returns>
    private static string? ValidateRetentionDays(string value)
    {
        return int.TryParse(value, out _)
            ? null
            : $"须为整数（如 {LogRetentionJob.DefaultRetentionDays}）；当前值「{value}」无法解析";
    }
}
