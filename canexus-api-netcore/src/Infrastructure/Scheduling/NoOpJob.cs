
using Quartz;

namespace Infrastructure.Scheduling;
/// <summary>
/// 内置示例空任务：执行本身不做任何业务，仅供演示与集成测试；
/// 执行日志由调度包装任务（JobExecuteWrapper）统一记录，单次执行产生一条执行历史。
/// </summary>
public class NoOpJob : IJob
{
    /// <summary>已登记的稳定白名单标识（写入 SysJob.JobType；不再写入 CLR 程序集限定名）。</summary>
    public const string JobKey = JobTypeResolver.NoOpJobKey;

    /// <summary>执行空任务（无业务动作）。</summary>
    /// <param name="context">执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
