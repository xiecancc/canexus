
using Quartz;

namespace Infrastructure.Scheduling;
/// <summary>
/// 调度任务类型解析器：统一 sys_job.job_type 字符串 → Quartz IJob 类型的解析入口
/// （调度管理器入调度前校验 / 执行包装器运行期解析 / 验证器合法性判定共用）。
/// <para>解析策略：先走 <see cref="Type.GetType(string, bool)"/>（支持程序集限定名）；
/// 未命中时遍历已加载程序集按「命名空间.类名」探测（短格式，覆盖单程序集部署的常见写法），
/// 消除仅写类全名时因缺少程序集限定名而解析失败的静默失效。</para>
/// </summary>
public static class JobTypeResolver
{
    /// <summary>解析任务类型：可解析且实现 Quartz IJob 时返回类型，否则返回 null。</summary>
    /// <param name="jobType">任务类型字符串（程序集限定名或命名空间.类名）。</param>
    /// <returns>目标 IJob 类型；不可解析时为 null。</returns>
    public static Type? Resolve(string? jobType)
    {
        if (string.IsNullOrWhiteSpace(jobType))
        {
            return null;
        }

        var targetType = Type.GetType(jobType, throwOnError: false);
        if (targetType is not null)
        {
            return ValidateIsJob(targetType) ? targetType : null;
        }

        // 短格式兜底：遍历已加载程序集探测「命名空间.类名」（单程序集部署下 job_type 常不带程序集限定名）
        targetType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(jobType, throwOnError: false))
            .FirstOrDefault(type => type is not null);
        return targetType is not null && ValidateIsJob(targetType) ? targetType : null;
    }

    /// <summary>判定解析结果是否为合法的 Quartz 任务类型（公开且可实现化，排除抽象类 / 泛型定义）。</summary>
    private static bool ValidateIsJob(Type targetType) =>
        typeof(IJob).IsAssignableFrom(targetType)
        && targetType is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false };
}
