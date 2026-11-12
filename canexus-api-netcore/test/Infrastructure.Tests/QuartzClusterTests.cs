
using System.Text.RegularExpressions;
using Infrastructure.Scheduling;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SqlSugar;

namespace Infrastructure.Tests;

/// <summary>
/// Quartz 集群模式集成测试（P1-A4）：双调度器实例共享同一 MySQL 持久化 JobStore，
/// 验证「同一任务被两个实例同时装载时，每个触发周期全局仅执行一次」（双实例部署不重复执行）。
/// <para>临时库（super_admin_quartz_test_&lt;guid&gt;）在测试结束后 DROP，不污染共享测试库。</para>
/// </summary>
[TestFixture]
public class QuartzClusterTests
{
    private string _tempDatabaseName = null!;
    private string _tempConnection = null!;
    private IScheduler _schedulerA = null!;
    private IScheduler _schedulerB = null!;

    /// <summary>一次性初始化：建独立临时库、建 Quartz 集群表与计数探针表，并装配两个调度器实例。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;
        _tempDatabaseName = $"super_admin_quartz_test_{Guid.NewGuid():N}";
        _tempConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", $"database={_tempDatabaseName}");

        // 临时库尚不存在：先用已知库（super_admin_test）建库，再切到临时库
        var adminConnection = Regex.Replace(connectionString, "(?i)database=[^;]+", "database=super_admin_test");
        using (var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection)))
        {
            await adminClient.Ado.ExecuteCommandAsync("CREATE DATABASE IF NOT EXISTS `" + _tempDatabaseName + "`");
        }

        // QRTZ_ 集群表（幂等，内部按 DbMaintenance ORM 元数据判存在）+ 计数探针表
        // 探针表显式 ENGINE=InnoDB：本服务器默认引擎为 MyISAM（无行锁 / DML 事务语义），与生产表引擎对齐
        await QuartzSchemaInitializer.EnsureSchemaAsync(_tempConnection);
        using (var tempClient = new SqlSugarScope(CreateConnectionConfig(_tempConnection)))
        {
            await tempClient.Ado.ExecuteCommandAsync("CREATE TABLE qrtz_count (Id CHAR(36) PRIMARY KEY) ENGINE=InnoDB");
        }

        _schedulerA = BuildSchedulingProvider().GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult();
        _schedulerB = BuildSchedulingProvider().GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult();
    }

    /// <summary>装配一个调度器实例（与生产 AddScheduling 同一入口：Quartz + 宿主服务 + 调度管理）。</summary>
    private IServiceProvider BuildSchedulingProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<DatabaseOption>().Configure(options => options.ConnectionString = _tempConnection);
        services.AddScheduling();
        services.AddTransient<QuartzCountingJob>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 验证集群语义：两个实例装载同一 JobKey + 同一 Cron 触发（生产 JobScheduleManager 每实例都会装载），
    /// 等待 12 秒（每 2 秒触发一次），断言总执行次数落在单实例合理区间（约 3~6 次）。
    /// <para>非集群（RAM JobStore）下两个实例各自持有独立触发 → 双倍执行（约 10~12 次），断言必失败。</para>
    /// </summary>
    [Test]
    public async Task TwoInstances_SharedJob_ExecutesOncePerFire()
    {
        var jobKey = new JobKey("sys_job_cluster_probe", "quartz-cluster-test");
        var jobDetail = JobBuilder.Create<QuartzCountingJob>()
            .WithIdentity(jobKey)
            .WithDescription("集群探针任务")
            .UsingJobData(QuartzCountingJob.ConnectionKey, _tempConnection)
            .StoreDurably()
            .Build();
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobKey.Name}_trigger", jobKey.Group)
            .WithCronSchedule("0/2 * * * * ?")
            .Build();

        // 双实例装载同一任务（幂等装载，与生产 JobScheduleManager 语义一致：先 DeleteJob 再 ScheduleJob）：
        // 集群下两个实例共享同一持久化存储，同一 JobKey 的触发器唯一 → 每触发周期全局仅执行一次
        await LoadJobAsync(_schedulerA, jobDetail, trigger);
        await LoadJobAsync(_schedulerB, jobDetail, trigger);
        await _schedulerA.Start();
        await _schedulerB.Start();

        await Task.Delay(TimeSpan.FromSeconds(12));

        var count = await CountRowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThanOrEqualTo(3), "任务应已按 cron 触发（至少 3 次）");
            Assert.That(count, Is.LessThanOrEqualTo(7), "双实例不应重复执行：集群下每个触发周期全局仅执行一次");
        });
    }

    /// <summary>幂等装载任务（生产 JobScheduleManager 装载语义：先删除再调度，双实例可安全重复装载）。</summary>
    /// <param name="scheduler">调度器实例。</param>
    /// <param name="jobDetail">任务定义。</param>
    /// <param name="trigger">触发器。</param>
    private static async Task LoadJobAsync(IScheduler scheduler, IJobDetail jobDetail, ITrigger trigger)
    {
        await scheduler.DeleteJob(jobDetail.Key);
        await scheduler.ScheduleJob(jobDetail, trigger);
    }

    /// <summary>统计探针表行数（每次任务执行插入一行）。</summary>
    private async Task<int> CountRowsAsync()
    {
        using var client = new SqlSugarScope(CreateConnectionConfig(_tempConnection));
        var count = await client.Ado.GetScalarAsync("SELECT COUNT(*) FROM qrtz_count");
        return Convert.ToInt32(count);
    }

    /// <summary>一次性清理：停掉两个调度器并删除临时库。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await _schedulerA.Shutdown();
        }
        catch
        {
            // 初始化失败时调度器可能未启动，清理失败不影响测试结果
        }

        try
        {
            await _schedulerB.Shutdown();
        }
        catch
        {
            // 同上
        }

        try
        {
            // 临时库删除走管理库连接（super_admin_test）；连接串经支撑类取环境变量，无内置回退
            var adminConnection = Regex.Replace(
                TestDatabaseSupport.TestConnectionString, "(?i)database=[^;]+", "database=super_admin_test");
            using var adminClient = new SqlSugarScope(CreateConnectionConfig(adminConnection));
            await adminClient.Ado.ExecuteCommandAsync("DROP DATABASE IF EXISTS `" + _tempDatabaseName + "`");
        }
        catch
        {
            // 初始化失败时临时库可能不存在，清理失败不影响测试结果
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

/// <summary>集群探针任务：每次执行向计数表插入一行（经 JobData 传入连接串）。</summary>
public sealed class QuartzCountingJob : IJob
{
    /// <summary>JobData 中连接串的键名。</summary>
    public const string ConnectionKey = "conn";

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var connectionString = context.MergedJobDataMap.GetString(ConnectionKey) ?? throw new InvalidOperationException("缺少连接串");
        using var client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        await client.Ado.ExecuteCommandAsync("INSERT INTO qrtz_count (Id) VALUES (UUID())");
    }
}
