
using Domain.Repositories;
using Domain.Services;
using Domain.Services.Auth;
using Domain.Services.Notifications;
using Domain.Services.Security;
using Domain.Services.Storage;
using Domain.Services.Users;
using Infrastructure.Auth;
using Infrastructure.Caching;
using Infrastructure.DataAccess;
using Infrastructure.Filtering;
using Infrastructure.Locking;
using Infrastructure.Notifications;
using Infrastructure.Repositories;
using Infrastructure.Scheduling;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Contexts;
using Infrastructure.Shared.Filtering;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;
using Infrastructure.Storage;
using Infrastructure.Tenants;
using Infrastructure.Users;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;
using SqlSugar;

namespace Infrastructure;
/// <summary>
/// 基础设施层依赖注册扩展（全部 DI 装配声明统一收敛于本文件，按功能分区组织）：
/// <list type="bullet">
/// <item>配置选项注册：<see cref="RegisterOptions"/> / <see cref="RegisterOption{TOption}"/>（启动时统一注册全部 Option）；</item>
/// <item>认证授权基座：<see cref="AddSecurity"/>；</item>
/// <item>定时任务调度：<see cref="AddScheduling"/>；</item>
/// <item>双层缓存：<see cref="AddCaching"/>（HybridCache + Redis）；</item>
/// <item>数据访问：<see cref="AddDataAccess"/>（SqlSugar 客户端 / 仓储 / 工作单元，客户端构造等行为逻辑见 DataAccess 模块的 SqlSugarClientFactory）；</item>
/// <item>CORS / 接口限流 / HTTPS 的服务与管道注册位于 API 层的 SecurityDependencyExtensions（宿主层横切关注点）。</item>
/// </list>
/// </summary>
public static class DependencyExtensions
{
    #region 配置选项注册

    /// <summary>
    /// 注册单个配置选项：绑定配置节 + 校验 + 启动即验（ValidateOnStart）+ 具体类型注册。
    /// <para>经 Bind 接入配置变更令牌，IOptionsMonitor&lt;TOption&gt; 支持热加载；
    /// 校验经 <see cref="OptionValidator{TOption}"/> 复用 <see cref="OptionBase.Validate"/>，
    /// 失败消息（含具体字段定位，如 "Jwt:Secret 长度不得小于 32 字符"）原样进入
    /// OptionsValidationException.Failures，并在启动时与每次（重）解析时触发、非法即终止流程。</para>
    /// </summary>
    /// <typeparam name="TOption">配置选项类型（须继承 OptionBase 且具备无参构造）。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="sectionName">配置节名称。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection RegisterOption<TOption>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOption : OptionBase, new()
    {
        // 必须用 Bind（而非在 Configure<IConfiguration> 委托内手动 Bind）：
        // 只有 Bind 会注册 IOptionsChangeTokenSource，配置文件 reloadOnChange 变更
        // 才会触发 IOptionsMonitor 刷新（热加载的关键）
        services.AddOptions<TOption>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();

        // 校验器：接入 OptionBase.Validate，字段级错误消息保留进 OptionsValidationException.Failures
        services.AddSingleton<IValidateOptions<TOption>, OptionValidator<TOption>>();

        // 具体类型注册：可直接注入 TOption，与 IOptions<TOption>.Value 为同一托管实例
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TOption>>().Value);

        return services;
    }

    /// <summary>
    /// 注册全部配置选项：显式调用 <see cref="RegisterOption{TOption}"/> 逐个注册（绑定 + 校验 + 启动即验 + 热加载）。
    /// <para>全部 Option 均应在宿主启动时获取且不依赖其他业务，故应在其他业务注册之前调用本方法一次；
    /// 新增 Option 时在此处显式追加一行注册。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection RegisterOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.RegisterOption<CacheOption>(configuration, "Cache");
        services.RegisterOption<DatabaseOption>(configuration, "Database");
        services.RegisterOption<JwtOption>(configuration, "Jwt");
        services.RegisterOption<CorsOption>(configuration, "Cors");
        services.RegisterOption<RateLimitOption>(configuration, "RateLimit");
        services.RegisterOption<HttpsOption>(configuration, "Https");
        services.RegisterOption<ForwardedOption>(configuration, "Forwarded");
        services.RegisterOption<FileStorageOption>(configuration, "FileStorage");
        services.RegisterOption<MailOption>(configuration, "Mail");
        services.RegisterOption<OutboxOption>(configuration, "Outbox");
        services.RegisterOption<MultiTenancyOption>(configuration, "MultiTenancy");
        services.RegisterOption<SchedulingOption>(configuration, "Scheduling");

        return services;
    }

    #endregion

    #region 认证授权基座

    /// <summary>注册认证授权基座能力：当前用户上下文、密码哈希、令牌服务与种子数据初始化器。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        // 当前用户上下文：单例 + IHttpContextAccessor，请求间天然隔离
        services.AddSingleton<IUserContext, HttpContextUserContext>();

        // 密码哈希（BCrypt）：无状态单例
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        // 令牌服务：依赖 scoped 仓储（refreshToken 持久化），注册为 scoped
        services.AddScoped<ITokenService, JwtTokenService>();

        // 令牌主题工厂：登录成功与刷新令牌轮换共用的用户角色装载逻辑
        services.AddScoped<ITokenSubjectFactory, TokenSubjectFactory>();

        // 令牌版本（安全戳）服务：用户级版本 + 会话级吊销集合，请求期经缓存校验（P1-A 令牌可撤销）
        services.AddScoped<ITokenVersionService, Infrastructure.Auth.TokenVersionService>();

        // 请求期令牌撤销校验：JwtBearer OnTokenValidated 事件调用，校验失败即 401
        services.AddScoped<ITokenValidationGuard, Infrastructure.Auth.TokenValidationGuard>();

        // 图形验证码：无状态单例（验证码内容经 ICache 双层缓存存取）
        services.AddSingleton<ICaptchaService, CaptchaService>();

        // 种子数据初始化器：依赖 ISqlSugarClient 与密码哈希
        services.AddSingleton<SeedDataInitializer>();

        return services;
    }

    #endregion

    #region 文件存储

    /// <summary>注册文件存储能力：本地磁盘实现（对象存储 Provider 后续按 FileStorage:Provider 配置扩展注入）。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        // 无状态单例：内部仅持有配置快照与路径计算，无共享可变状态
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }

    #endregion

    #region 通知投递

    /// <summary>注册通知投递渠道：邮件渠道恒注册（内部按 Mail:Enabled 静默跳过，避免配置热切换时的注册时序问题）。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton<INotificationChannel, SmtpEmailChannel>();

        return services;
    }

    #endregion

    #region 定时任务调度

    /// <summary>注册定时任务调度能力：Quartz 调度器（MySQL 持久化 JobStore + 集群模式）、宿主服务与调度管理实现。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddScheduling(this IServiceCollection services)
    {
        // Quartz 集群存储初始化（P1-A4）：幂等确保 QRTZ_ 表存在，必须先于调度器宿主启动
        services.AddHostedService<QuartzSchemaInitializer>();

        // Quartz 调度器：MySQL 持久化 JobStore（JobStoreTX）+ 集群模式（P1-A4）。
        // 集群语义：任务定义 / 触发器 / 已触发记录全部持久化于共享 MySQL，实例间经 QRTZ_LOCKS 行锁竞争触发，
        // 同一触发周期全局仅一个实例执行——双实例（多副本）部署任务不重复执行。
        services.AddQuartz();

        // 调度配置默认值：未走宿主 RegisterOptions 的场景（如独立装配调度器的集成测试）回退到默认参数；
        // 宿主启动时 RegisterOptions 已注册 SchedulingOption，此处 TryAdd 不会覆盖
        services.TryAddSingleton(new SchedulingOption());
        services.AddOptions<QuartzOptions>()
            .Configure<SchedulingOption>((quartz, scheduling) =>
            {
                // 线程池显式配置（P1-C）：避免默认 10 线程与业务并发量错配导致的触发排队
                quartz["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool";
                quartz["quartz.threadPool.threadCount"] = scheduling.ThreadCount.ToString();
            });
        services.AddOptions<QuartzOptions>()
            .Configure<IOptions<DatabaseOption>>((quartz, databaseOptions) =>
            {
                QuartzMySqlProvider.EnsureRegistered();
                var database = databaseOptions.Value;

                quartz.SchedulerName = "SuperAdminScheduler";
                // 集群实例 ID 自动生成（主机名 + 时间戳，多实例唯一）
                quartz.SchedulerId = "AUTO";
                quartz["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX";
                // driverDelegateType：Quartz 3.22.9 的 StdAdoDelegate 存在布尔列读写不对称——写入侧以 "1"/"0" 落库，
                // 而读取侧 GetBooleanFromDbValue 走 Convert.ToBoolean（仅认 "True"/"False"）抛 FormatException
                //（实测 "String '1' was not recognized as a valid Boolean"，任务装载 / 触发器删除均受影响）。
                // 采用自定义 SuperAdminMySqlDelegate：读取侧对称适配 "1"/"0"/"Y"/"N"，兼容存量与新增数据
                quartz["quartz.jobStore.driverDelegateType"] = "Infrastructure.Scheduling.SuperAdminMySqlDelegate, Infrastructure";
                quartz["quartz.jobStore.clustered"] = "true";
                quartz["quartz.jobStore.clusterCheckinInterval"] = "15000";
                quartz["quartz.jobStore.tablePrefix"] = "QRTZ_";
                quartz["quartz.jobStore.dataSource"] = "default";
                quartz["quartz.jobStore.useProperties"] = "false";
                // 序列化器：非 RAM JobStore 必须显式指定；内置 binary 基于 BinaryFormatter（.NET 10 已移除，不可用），
                // 采用 json（Quartz.Serialization.Json 独立包，Infrastructure 已引用）——JobDataMap 以 JSON 存 QRTZ_JOB_DETAILS.JOB_DATA
                quartz["quartz.serializer.type"] = "json";
                quartz["quartz.dataSource.default.connectionString"] = database.ConnectionString;
                quartz["quartz.dataSource.default.provider"] = QuartzMySqlProvider.ProviderName;
            });
        // 优雅停机（P1-C）：主机停止时等待运行中的任务完成（受宿主关闭超时上限保护），避免任务被中途中断；
        // 若部署侧关闭超时短于任务耗时时，应同时调大宿主 / 容器 stop timeout
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // 定时任务调度管理（依赖 scoped 仓储，随请求作用域创建）
        services.AddScoped<IJobScheduleManager, JobScheduleManager>();

        // 调度器运行信息提供者（监控端点消费）
        services.AddScoped<ISchedulerInfoProvider, SchedulerInfoProvider>();

        // 缓存运行信息提供者（监控端点消费）
        services.AddScoped<ICacheMonitorProvider, CacheMonitorProvider>();

        return services;
    }

    #endregion

    #region 双层缓存

    /// <summary>
    /// 注册 HybridCache 双层缓存（L1 进程内存 + L2 分布式缓存）与 <see cref="ICache"/> 端口。
    /// <para>CacheOption 由启动时的 RegisterOptions 统一注册，本方法全部经 IOptionsMonitor 从注册结果获取：
    /// IDistributedCache 以工厂注册——RedisEnabled=true 时为 Redis（L2 分布式缓存），
    /// false 时为进程内存实现（无 Redis 依赖，等效纯 L1 语义），分支在首次解析时定型、不支持热加载；
    /// MaximumPayloadBytes 经 Options 管道从注册结果读取（HybridCache 内部启动捕获，不支持热加载）。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddCaching(this IServiceCollection services)
    {
        // L2 分布式缓存：从已注册的 CacheOption（IOptionsMonitor）读取开关与连接信息，
        // 不再手动绑定配置节；分支随首次解析定型，避免服务图在 Build 后变动
        services.AddSingleton<IDistributedCache>(serviceProvider =>
        {
            var option = serviceProvider.GetRequiredService<IOptionsMonitor<CacheOption>>().CurrentValue;
            return option.RedisEnabled
                ? new RedisCache(Options.Create(new RedisCacheOptions
                {
                    Configuration = option.RedisConfiguration,
                    InstanceName = option.InstanceName
                }))
                : new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        });

        services.AddHybridCache();

        // 分布式锁：Redis 启用时 SET NX PX 原子锁（令牌校验释放），未启用降级进程内信号量；
        // 保护跨实例读-改-写关键区（登录失败计数 / 权限版本号 / 种子初始化）
        services.AddSingleton<IDistributedLock, DistributedLock>();

        // 单条负载上限：经 Options 管道从已注册的 CacheOption 获取（HybridCache 内部启动捕获，不支持热加载）
        services.AddOptions<HybridCacheOptions>()
            .Configure<IOptionsMonitor<CacheOption>>((hybrid, monitor) =>
                hybrid.MaximumPayloadBytes = monitor.CurrentValue.MaximumPayloadBytes);

        services.AddSingleton<ICache, Cache>();
        return services;
    }

    #endregion

    #region 数据访问

    /// <summary>
    /// 注册数据访问能力：SqlSugar 客户端（单例 SqlSugarScope）、通用 / 聚合 / 树形仓储、工作单元、上下文服务与初始化器。
    /// <para>配置取自 DatabaseOption（由启动时的 RegisterOptions 统一注册，可直接注入具体类型或 IOptionsMonitor）；
    /// 客户端构造与 Aop / 过滤器装配等行为逻辑见 <see cref="SqlSugarClientFactory"/>（DataAccess 模块内）。</para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddDataAccess(this IServiceCollection services)
    {
        // SqlSugar 审计 Aop 依赖当前用户上下文：数据访问可独立使用（如集成测试），已注册时跳过
        services.TryAddSingleton<IUserContext, HttpContextUserContext>();
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<IDataFilter, DataFilter>();
        services.AddHttpContextAccessor();
        services.AddSingleton<ISqlSugarClient>(serviceProvider =>
            SqlSugarClientFactory.Create(serviceProvider.GetRequiredService<IOptionsMonitor<DatabaseOption>>(), serviceProvider));
        services.AddScoped(typeof(IDomainRepository<>), typeof(DomainRepository<>));
        services.AddScoped(typeof(IAggregateRepository<>), typeof(AggregateRepository<>));
        services.AddScoped(typeof(IAggregateTreeRepository<>), typeof(AggregateTreeRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOperLogRepository, OperLogRepository>();
        services.AddScoped<ISysRefreshTokenRepository, SysRefreshTokenRepository>();
        services.AddScoped<ILoginLogRepository, LoginLogRepository>();
        services.AddScoped<IJobLogRepository, JobLogRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<ITenantAccessValidator, TenantAccessValidator>();
        services.AddSingleton<SchemaMigrator>();
        // 增量迁移清单（P1-A3）：新增迁移按 ISchemaMigration 实现并在此追加
        // 增量迁移清单（P1-A3）：新增迁移按 ISchemaMigration 实现并在此逐个注册
        // （DbInitializer 以 IEnumerable<ISchemaMigration> 注入——逐个注册才能被 DI 集合解析发现；
        // 注册 ISchemaMigration[] 单例会因 IEnumerable 特殊解析规则得到空集合）
        services.AddSingleton<ISchemaMigration, Infrastructure.DataAccess.Migrations.AddPostTablesMigration>();
        services.AddSingleton<ISchemaMigration, Infrastructure.DataAccess.Migrations.AddOperLogAuditColumnsMigration>();
        // 请求上下文（P0 默认审计）：供 AuditLogBehavior 读取链路 TraceId、并与 OperLogActionFilter 协同去重
        services.AddSingleton<IOperationContext, Infrastructure.Logging.HttpContextOperationContext>();
        services.AddSingleton<DbInitializer>();

        return services;
    }

    #endregion
}

/// <summary>
/// OptionBase 校验适配器：把断言式校验（<see cref="OptionBase.Validate"/> 抛异常）接入 Options 校验管道。
/// <para>以 ValidateOptionsResult 返回失败而非抛异常，字段级错误消息
/// （如 "Jwt:Secret 长度不得小于 32 字符"）原样进入 OptionsValidationException.Failures，
/// 由 Options 管道统一聚合与包装（启动 ValidateOnStart / 每次解析 / 热加载重校验共用）。</para>
/// </summary>
/// <typeparam name="TOption">配置选项类型。</typeparam>
internal sealed class OptionValidator<TOption> : IValidateOptions<TOption>
    where TOption : OptionBase
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TOption options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException exception)
        {
            // ArgumentOutOfRangeException 亦派生自 ArgumentException，一并覆盖
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
