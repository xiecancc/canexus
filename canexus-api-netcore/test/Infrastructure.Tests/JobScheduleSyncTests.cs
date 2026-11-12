
using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Services;
using Domain.Shared.Enums;
using Infrastructure.Scheduling;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Logging;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// 定时任务启动同步测试（P1-C）：验证「差异比对后 add / update / delete」的启动装载语义——
/// 定义未变时二次启动不重置 Trigger（保留下次触发时间）、Cron 变更时正确更新、
/// 多副本（多实例）并发启动不产生重复 Job / Trigger、库中停用任务会被清理出调度器。
/// <para>临时库（super_admin_job_sync_test_&lt;guid&gt;）测试结束后 DROP，不污染共享测试库。</para>
/// </summary>
[TestFixture]
public class JobScheduleSyncTests
{
    private const string JobType = NoOpJob.JobKey;

    private string _tempDatabaseName = null!;
    private string _tempConnection = null!;
    private SqlSugarScope _client = null!;
    private ServiceProvider _provider = null!;
    private ServiceProvider _secondProvider = null!;

    /// <summary>一次性初始化：建独立临时库、建 sys_job 表，并装配两个共享同一 JobStore 的调度宿主（模拟两副本）。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;
        _tempDatabaseName = $"super_admin_job_sync_test_{Guid.NewGuid():N}";
        _tempConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", $"database={_tempDatabaseName}");

        var adminConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", "database=super_admin_test");
        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync("CREATE DATABASE IF NOT EXISTS `" + _tempDatabaseName + "`");
        }

        // Quartz 持久化 JobStore 表（幂等）
        await QuartzSchemaInitializer.EnsureSchemaAsync(_tempConnection);

        _client = new SqlSugarScope(CreateConnectionConfig(_tempConnection));
        _client.CodeFirst.InitTables<SysJob>();

        _provider = BuildProvider();
        _secondProvider = BuildProvider();
    }

    /// <summary>定义未变化时二次启动：不重建 Trigger，下次触发时间保持原值（滚动发布不丢触发）。</summary>
    [Test]
    public async Task SecondStart_UnchangedDefinition_KeepsTriggerNextFireTime()
    {
        var job = await CreateJobAsync("unchanged", "0 0 0 1 1 ? 2035");
        var manager = _provider.GetRequiredService<IJobScheduleManager>();
        await manager.InitializeAsync();

        var scheduler = await _provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var jobKey = BuildJobKey(job);
        var triggerBefore = (await scheduler.GetTriggersOfJob(jobKey)).Single();
        var nextFireBefore = triggerBefore.GetNextFireTimeUtc();

        // 第二次启动（模拟滚动发布的新副本再次执行同步）
        await manager.InitializeAsync();

        var triggersAfter = await scheduler.GetTriggersOfJob(jobKey);
        Assert.Multiple(() =>
        {
            Assert.That(triggersAfter, Has.Count.EqualTo(1), "同一任务应只有一个触发器（不重复、不丢失）");
            Assert.That(triggersAfter.Single().GetNextFireTimeUtc(), Is.EqualTo(nextFireBefore), "定义未变时不得重置下次触发时间");
        });
    }

    /// <summary>Cron 变更时：按同一 TriggerKey 重新调度，表达式与下次触发时间正确更新，且不产生重复触发器。</summary>
    [Test]
    public async Task ChangedCron_UpdatesTriggerInPlace()
    {
        var job = await CreateJobAsync("changed-cron", "0 0 0 1 1 ? 2035");
        var manager = _provider.GetRequiredService<IJobScheduleManager>();
        await manager.InitializeAsync();

        var scheduler = await _provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var jobKey = BuildJobKey(job);
        var before = (ICronTrigger)(await scheduler.GetTriggersOfJob(jobKey)).Single();

        var newCron = "0 0 0 1 6 ? 2035";
        await _client.Updateable<SysJob>()
            .SetColumns(it => it.CronExpression == newCron)
            .Where(it => it.Id == job.Id)
            .ExecuteCommandAsync();
        await manager.InitializeAsync();

        var triggersAfter = await scheduler.GetTriggersOfJob(jobKey);
        var after = (ICronTrigger)triggersAfter.Single();
        Assert.Multiple(() =>
        {
            Assert.That(triggersAfter, Has.Count.EqualTo(1), "Cron 变更应复用同一触发器，不产生重复");
            Assert.That(after.CronExpressionString, Is.EqualTo(newCron), "触发器 Cron 应更新为新表达式");
            Assert.That(after.GetNextFireTimeUtc(), Is.Not.EqualTo(before.GetNextFireTimeUtc()), "下次触发时间应按新 Cron 重新计算");
        });
    }

    /// <summary>两个实例并发执行启动同步：共享 JobStore 中同一任务仅一份 JobDetail 与一个 Trigger。</summary>
    [Test]
    public async Task TwoInstances_ConcurrentStartup_NoDuplicateJobOrTrigger()
    {
        var job = await CreateJobAsync("two-instances", "0 0 0 1 1 ? 2036");
        var managerA = _provider.GetRequiredService<IJobScheduleManager>();
        var managerB = _secondProvider.GetRequiredService<IJobScheduleManager>();

        await Task.WhenAll(managerA.InitializeAsync(), managerB.InitializeAsync());

        var schedulerA = await _provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var schedulerB = await _secondProvider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var jobKey = BuildJobKey(job);

        var detailsA = await schedulerA.GetJobDetail(jobKey);
        var triggersA = await schedulerA.GetTriggersOfJob(jobKey);
        var triggersB = await schedulerB.GetTriggersOfJob(jobKey);

        Assert.Multiple(() =>
        {
            Assert.That(detailsA, Is.Not.Null, "任务应已入调度");
            Assert.That(triggersA, Has.Count.EqualTo(1), "两实例并发同步后触发器应唯一");
            Assert.That(triggersB, Has.Count.EqualTo(1), "两实例查看共享 JobStore 应看到同一唯一触发器");
        });

        var jobKeys = await schedulerA.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(job.JobGroup));
        Assert.That(jobKeys.Count(key => key.Name == jobKey.Name), Is.EqualTo(1), "共享 JobStore 中同一任务不得出现多份");
    }

    /// <summary>库中任务停用后再次同步：对应调度任务被删除（不残留、不继续触发）。</summary>
    [Test]
    public async Task DisabledJob_RemovedFromSchedulerOnNextSync()
    {
        var job = await CreateJobAsync("disabled", "0 0 0 1 1 ? 2035");
        var manager = _provider.GetRequiredService<IJobScheduleManager>();
        await manager.InitializeAsync();

        var scheduler = await _provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var jobKey = BuildJobKey(job);
        Assert.That(await scheduler.GetJobDetail(jobKey), Is.Not.Null, "运行中任务应已入调度");

        await _client.Updateable<SysJob>()
            .SetColumns(it => it.Status == CommonStatus.Disabled)
            .Where(it => it.Id == job.Id)
            .ExecuteCommandAsync();
        await manager.InitializeAsync();

        Assert.That(await scheduler.GetJobDetail(jobKey), Is.Null, "停用任务应从调度器删除");
    }

    /// <summary>P2-7 结果校验口径：启动同步后，库中启用任务均已真实存在于调度器（逐 Job 核对）。</summary>
    [Test]
    public async Task SyncResult_AllEnabledJobs_PresentInScheduler()
    {
        var jobs = new[]
        {
            await CreateJobAsync("verify-a", "0 0 0 1 1 ? 2037"),
            await CreateJobAsync("verify-b", "0 0 0 1 1 ? 2037")
        };
        var manager = _provider.GetRequiredService<IJobScheduleManager>();
        await manager.InitializeAsync();

        var scheduler = await _provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        foreach (var job in jobs)
        {
            var detail = await scheduler.GetJobDetail(BuildJobKey(job));
            Assert.That(detail, Is.Not.Null, $"启用任务 {job.JobName} 同步后应真实存在于调度器");
        }
    }

    /// <summary>一次性清理：停掉两个调度器、删除临时库。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await ShutdownSchedulerAsync(_provider);
        await ShutdownSchedulerAsync(_secondProvider);
        _provider?.Dispose();
        _secondProvider?.Dispose();

        // Quartz 的 LogContext 为进程级静态：本夹具销毁服务提供者后，若其 LoggerFactory 仍被 Quartz 全局持有，
        // 后续夹具创建调度器会访问已释放对象（ObjectDisposedException）。重置为不释放的空日志工厂，
        // 避免夹具间通过 Quartz 静态状态相互污染
        LogContext.SetCurrentLogProvider(NullLoggerFactory.Instance);

        try
        {
            _client.Dispose();
        }
        catch
        {
            // 初始化失败时客户端可能未创建
        }

        try
        {
            var adminConnection = Regex.Replace(
                TestDatabaseSupport.TestConnectionString, "(?i)database=[^;]+", "database=super_admin_test");
            using var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection));
            await adminClient.Ado.ExecuteCommandAsync("DROP DATABASE IF EXISTS `" + _tempDatabaseName + "`");
        }
        catch
        {
            // 初始化失败时临时库可能不存在
        }
    }

    /// <summary>装配一个调度宿主（与生产 RegisterOptions / AddDataAccess / AddCaching / AddScheduling 同一入口）。</summary>
    /// <returns>服务提供者。</returns>
    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = _tempConnection,
                ["Database:MigrationsEnabled"] = "false",
                ["Cache:RedisEnabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterOptions(configuration);
        services.AddCaching();
        services.AddDataAccess();
        services.AddScheduling();
        return services.BuildServiceProvider();
    }

    /// <summary>插入一个启用状态的任务定义。</summary>
    /// <param name="nameSuffix">任务名后缀（避免测试间重名）。</param>
    /// <param name="cron">Cron 表达式。</param>
    /// <returns>已持久化的任务。</returns>
    private async Task<SysJob> CreateJobAsync(string nameSuffix, string cron)
    {
        var job = new SysJob
        {
            Id = Guid.NewGuid(),
            JobName = $"P1-C-{nameSuffix}",
            JobGroup = $"p1c-{nameSuffix}",
            CronExpression = cron,
            JobType = JobType,
            Status = CommonStatus.Enabled,
            Concurrent = true
        };
        await _client.Insertable(job).ExecuteCommandAsync();
        return job;
    }

    /// <summary>构造调度键（与 JobScheduleManager 约定一致）。</summary>
    /// <param name="job">任务定义。</param>
    /// <returns>JobKey。</returns>
    private static JobKey BuildJobKey(SysJob job) => new($"sys_job_{job.Id}", job.JobGroup);

    /// <summary>停止指定宿主中的调度器（初始化失败时忽略）。</summary>
    /// <param name="provider">服务提供者。</param>
    private static async Task ShutdownSchedulerAsync(IServiceProvider provider)
    {
        try
        {
            var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
            if (!scheduler.IsShutdown)
            {
                await scheduler.Shutdown();
            }
        }
        catch
        {
            // 初始化失败时调度器可能未创建，清理失败不影响测试结果
        }
    }

    /// <summary>构造与生产一致（SqlSugarClientFactory 口径）的连接配置。</summary>
    /// <param name="connectionString">连接串。</param>
    /// <returns>连接配置。</returns>
    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.PropertyType == typeof(string) || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    {
                        column.IsNullable = true;
                    }
                }
            }
        };
    }
}
