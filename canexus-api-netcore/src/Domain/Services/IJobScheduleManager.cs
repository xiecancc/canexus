
using Domain.Entities;

namespace Domain.Services;
/// <summary>
/// 定时任务调度管理（领域服务端口）：负责把 <see cref="SysJob"/> 定义同步到底层调度器（Quartz）。
/// <para>抽象定义在领域层（仅依赖实体），由基础设施层提供 Quartz 实现；
/// 应用层经此端口操作调度，不感知具体调度技术。</para>
/// </summary>
public interface IJobScheduleManager
{
    /// <summary>启动装载：加载全部运行中（Status=1）的任务加入调度（应用启动时在种子数据之后调用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>把任务加入调度（幂等替换；用于创建 / 修改 / 启用运行中任务）。</summary>
    /// <param name="job">任务定义。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task ScheduleAsync(SysJob job, CancellationToken cancellationToken = default);

    /// <summary>把任务移出调度（用于停用 / 删除任务）。</summary>
    /// <param name="jobId">任务 Id。</param>
    /// <param name="jobGroup">任务分组（JobKey 的一部分）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RemoveAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken = default);

    /// <summary>立即触发一次执行（不改变既定 Cron 调度）。</summary>
    /// <param name="job">任务定义。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RunOnceAsync(SysJob job, CancellationToken cancellationToken = default);

    /// <summary>校验 Cron 表达式是否合法（由底层调度器判定）。</summary>
    /// <param name="cronExpression">Cron 表达式。</param>
    /// <returns>合法返回 true。</returns>
    public bool IsValidCronExpression(string cronExpression);

    /// <summary>校验任务类型是否可用：程序集限定名可解析且实现底层调度器的任务接口。</summary>
    /// <param name="jobType">已登记业务作业白名单标识（如 noop）。</param>
    /// <returns>可用返回 true。</returns>
    public bool IsValidJobType(string jobType);
}
