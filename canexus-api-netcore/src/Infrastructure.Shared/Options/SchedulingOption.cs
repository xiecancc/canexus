
namespace Infrastructure.Shared.Options;
/// <summary>
/// Quartz 调度配置（P1-C）：线程池大小、启动同步锁参数、misfire 处理策略与优雅停机。
/// <para>线程池与 misfire 策略在调度器初始化时生效（不支持热加载）；同步锁参数仅影响启动装载，
/// 单副本部署无 Redis 时降级为进程内锁，多副本部署需 Redis 才能保证"仅一个副本执行同步"。</para>
/// </summary>
public sealed class SchedulingOption : OptionBase
{
    /// <summary>
    /// Quartz 工作线程数（默认 10）：并发执行的任务数上限；应不小于可能同时触发的关键任务数，
    /// 过小会导致触发排队（misfire）。
    /// </summary>
    public int ThreadCount { get; set; } = 10;

    /// <summary>
    /// 启动同步锁等待时间（秒，默认 10）：滚动发布时各副本同时启动，未抢到锁的副本等待该时长后放弃同步
    ///（持锁副本会完成全量差异同步，放弃不会导致任务缺失）。
    /// </summary>
    public int SyncLockWaitSeconds { get; set; } = 10;

    /// <summary>
    /// 启动同步锁租约（秒，默认 120）：持锁副本崩溃后锁自动释放；应大于一次全量差异同步的最长耗时。
    /// </summary>
    public int SyncLockLeaseSeconds { get; set; } = 120;

    /// <summary>
    /// misfire 处理策略（默认 FireAndProceed）：错过触发点后的补偿方式。
    /// <list type="bullet">
    /// <item>FireAndProceed：立即补触发一次并继续后续计划（适合幂等、需尽快执行的同步类任务）；</item>
    /// <item>RescheduleNextWithRemainingCount：不补触发，按剩余次数重新计算下一次触发（适合不可重复执行的业务任务）。</item>
    /// </list>
    /// </summary>
    public string MisfireInstruction { get; set; } = "FireAndProceed";

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureInRange(ThreadCount, 1, 500, nameof(ThreadCount), "Scheduling:ThreadCount 必须在 1-500 之间");
        EnsureGreaterThanZero(SyncLockWaitSeconds, nameof(SyncLockWaitSeconds), "Scheduling:SyncLockWaitSeconds 必须大于 0");
        EnsureGreaterThanZero(SyncLockLeaseSeconds, nameof(SyncLockLeaseSeconds), "Scheduling:SyncLockLeaseSeconds 必须大于 0");
        Ensure(
            string.Equals(MisfireInstruction, "FireAndProceed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(MisfireInstruction, "RescheduleNextWithRemainingCount", StringComparison.OrdinalIgnoreCase),
            nameof(MisfireInstruction),
            "Scheduling:MisfireInstruction 仅支持 FireAndProceed / RescheduleNextWithRemainingCount");
    }
}
