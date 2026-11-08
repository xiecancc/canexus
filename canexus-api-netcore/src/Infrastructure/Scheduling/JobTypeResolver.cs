
using Quartz;

namespace Infrastructure.Scheduling;
/// <summary>
/// 调度任务类型白名单：sys_job.job_type 只允许写入已登记业务作业的稳定标识，拒绝任何 CLR 程序集限定名。
/// <para>写入侧（创建 / 更新 / 入调度）一律走 <see cref="IsAllowedInput"/>；执行侧走 <see cref="Resolve"/>，
/// 额外兼容已登记的历史类型名，保证存量库中的旧作业在升级后仍可执行，但不会解析白名单外的任意类型。</para>
/// </summary>
public static class JobTypeResolver
{
    /// <summary>内置示例空任务的稳定标识（写入 sys_job.job_type 的合法值）。</summary>
    public const string NoOpJobKey = "noop";

    /// <summary>允许写入的稳定标识 → 业务作业类型。</summary>
    private static readonly Dictionary<string, Type> AllowedJobs = new(StringComparer.Ordinal)
    {
        [NoOpJobKey] = typeof(NoOpJob)
    };

    /// <summary>历史类型名 → 业务作业类型：仅供执行存量数据时兼容，写入侧一律拒绝。</summary>
    private static readonly Dictionary<string, Type> LegacyJobAliases = new(StringComparer.Ordinal)
    {
        ["Infrastructure.Scheduling.NoOpJob, Infrastructure"] = typeof(NoOpJob),
        ["Infrastructure.Scheduling.NoOpJob"] = typeof(NoOpJob)
    };

    /// <summary>全部允许写入的稳定标识（用于验证器与调度端口的错误提示）。</summary>
    /// <returns>稳定标识集合。</returns>
    public static IReadOnlyCollection<string> AllowedKeys => AllowedJobs.Keys.ToArray();

    /// <summary>判定任务类型字符串是否为允许写入的稳定标识。</summary>
    /// <param name="jobType">任务类型字符串。</param>
    /// <returns>属于白名单返回 true。</returns>
    public static bool IsAllowedInput(string? jobType) =>
        !string.IsNullOrWhiteSpace(jobType) && AllowedJobs.ContainsKey(jobType.Trim());

    /// <summary>
    /// 解析可执行的业务作业类型：仅查白名单与已登记历史别名，不再使用
    /// <c>Type.GetType</c> 或程序集扫描，杜绝反射执行白名单外的任意已加载类型。
    /// </summary>
    /// <param name="jobType">任务类型字符串。</param>
    /// <returns>目标 IJob 类型；不在白名单内返回 null。</returns>
    public static Type? Resolve(string? jobType)
    {
        if (string.IsNullOrWhiteSpace(jobType))
        {
            return null;
        }

        var key = jobType.Trim();
        if (AllowedJobs.TryGetValue(key, out var allowed))
        {
            return ValidateIsJob(allowed) ? allowed : null;
        }

        if (LegacyJobAliases.TryGetValue(key, out var legacy))
        {
            return ValidateIsJob(legacy) ? legacy : null;
        }

        return null;
    }

    /// <summary>判定登记类型是否为合法的 Quartz 任务类型（公开且可实现化，排除抽象类 / 泛型定义）。</summary>
    private static bool ValidateIsJob(Type targetType) =>
        typeof(IJob).IsAssignableFrom(targetType)
        && targetType is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false };
}
