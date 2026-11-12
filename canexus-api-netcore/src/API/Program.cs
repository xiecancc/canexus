
using API.Authorization;
using API.Extensions;
using API.Filters;
using API.HealthChecks;
using API.Middlewares;
using Application;
using Domain.Services;
using Domain.Shared.Localization;
using Domain.Shared.Results;
using Infrastructure;
using Infrastructure.DataAccess;
using Infrastructure.Scheduling;
using Infrastructure.Shared.DataAccess;
using Infrastructure.Shared.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;

namespace API;
/// <summary>应用程序入口。</summary>
public class Program
{
    /// <summary>Main 方法：配置 Serilog、构建并运行 Web 应用。</summary>
    /// <param name="args">命令行参数。</param>
    public static async Task Main(string[] args)
    {
        // 引导日志：宿主启动阶段（配置尚未加载时）先输出到控制台
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("正在启动 Super Admin API……");
            var builder = WebApplication.CreateBuilder(args);

            // Serilog：从 appsettings.json 读取完整配置（控制台 + logs/*.txt 滚动文件）
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            // Development 环境允许 JWT 密钥回退到开发专用值，但必须输出明确告警，不允许静默使用弱密钥。
            var jwtSecret = builder.Configuration["Jwt:Secret"];
            var insecureJwtSecret = string.IsNullOrWhiteSpace(jwtSecret)
                || string.Equals(jwtSecret, JwtOption.InsecureDefaultSecret, StringComparison.Ordinal);
            if (builder.Environment.IsDevelopment() && insecureJwtSecret)
            {
                Log.Warning("当前为 Development 环境且 Jwt:Secret 未配置或仍为默认不安全值，已启用开发回退密钥；生产环境必须显式配置至少 32 字节的独立随机密钥");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = JwtOption.DevelopmentFallbackSecret
                });
            }

            // 配置选项：启动时统一注册全部 Option（继承 OptionBase，按类名约定映射配置节；
            // 绑定 + 校验 + 热加载 + 启动即验），不依赖任何业务注册，须先于业务模块执行
            builder.Services.RegisterOptions(builder.Configuration);

            // 请求体上限与业务配置对齐：Kestrel 全局上限 = FileStorage:MaxSizeMB + 1MB（multipart 边界开销），
            // 使超过业务上限的请求体在进入业务校验前即被线级拒绝（修复原先端点 [RequestSizeLimit] 1GB 与业务上限 10MB 错配）
            var maxUploadMb = builder.Configuration.GetValue("FileStorage:MaxSizeMB", 10);
            builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = (maxUploadMb + 1) * 1024L * 1024);

            // 控制器与全局过滤器：权限校验（RequirePermission）+ 操作日志（[OperLog] 特性）
            builder.Services.AddControllers(options =>
            {
                options.Filters.Add<PermissionAuthorizationFilter>();
                options.Filters.Add<OperLogActionFilter>();
            });

            // Swagger 文档：OpenAPI JSON + Swagger UI（含 Bearer 鉴权方案）
            builder.Services.AddSwaggerDocumentation();

            // 双层缓存：L1 进程内存（短过期）+ L2 分布式缓存（长过期，CacheOption.RedisEnabled 开启后为 Redis）
            builder.Services.AddCaching();

            // 数据访问：SqlSugar 客户端、通用仓储、工作单元（CodeFirst 建库建表在启动时执行）
            builder.Services.AddDataAccess();

            // 认证授权基座：当前用户上下文、密码哈希、令牌服务、种子数据初始化器
            builder.Services.AddSecurity();

            // 文件存储：本地磁盘实现（IFileStorage 端口，上传白名单与大小上限经 FileStorage 配置控制）
            builder.Services.AddStorage();

            // 通知投递渠道：邮件等站外载体（渠道按 Mail:Enabled 静默跳过，失败不阻塞主流程）
            builder.Services.AddNotifications();

            // JWT Bearer 认证与授权
            builder.Services.AddJwtAuthentication();

            // HTTPS 安全传输参数（HSTS max-age 与重定向端口；经 UseApiHttps 挂载，HttpOption 控制开关）
            builder.Services.AddApiHttps();

            // CORS 跨域策略（命名策略，经 UseApiCors 挂载；CorsOption.Enabled=false 时不生效）
            builder.Services.AddApiCors();

            // 接口限流：全局可选 + 认证端点（验证码/登录/刷新令牌）固定窗口，被限流返回统一 Result 结构
            builder.Services.AddApiRateLimiting();

            // 应用服务：认证、菜单路由、验证码、权限校验、数据范围过滤
            builder.Services.AddApplicationServices();

            // API 版本化：默认 v1.0，未指定版本回退默认，响应头声明支持集（零破坏落法）
            builder.Services.AddApiVersioningSupport();

            // 请求本地化：zh-CN 默认 + en-US，按 Accept-Language 协商（业务异常消息随文化翻译）
            builder.Services.AddApiLocalization();

            // 定时任务调度：Quartz 调度器与 IJobScheduleManager（基础设施层实现）
            builder.Services.AddScheduling();

            // Outbox 后台投递 worker（P1-A2）：轮询待处理领域事件并投递（Outbox:Enabled 控制启停）
            builder.Services.AddHostedService<BackgroundServices.OutboxDeliveryWorker>();

            // 健康检查：liveness / readiness 检查项（MySQL / 分布式缓存 / Quartz），经 MapHealthChecks 暴露
            builder.Services.AddApiHealthChecks();

            // 可观测性：OpenTelemetry Trace + Metrics（默认关闭，Otel:Enabled 开启；OTLP 导出，Otel:OtlpEndpoint 指定端点）
            builder.Services.AddOpenTelemetrySupport(builder.Configuration);

            // 模型校验失败（含后续 FluentValidation 集成）统一返回 Result 结构，HTTP 400
            builder.Services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var messages = context.ModelState
                        .Where(pair => pair.Value is { Errors.Count: > 0 })
                        .SelectMany(pair => pair.Value!.Errors
                            .Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? pair.Key : error.ErrorMessage))
                        .ToList();

                    var result = Result.Fail(ErrorCodes.CommonValidationFailed,
                        ErrorMessageLocalizer.Localize(ErrorCodes.CommonValidationFailed, string.Join("；", messages)));
                    return new BadRequestObjectResult(result);
                };
            });

            var app = builder.Build();

            // 启动期配置基线：非 Development 校验密钥类配置，Production 额外校验传输 / 限流 / 租户 /
            // 转发头 / L2 缓存基线，一次性列出全部不合规项后阻止启动（避免逐项试错式上线）。
            // 必须在 Build() 之后用 app.Configuration 校验：WebApplicationFactory.ConfigureAppConfiguration
            // 注入的测试密钥在 Build() 时才合并进 ConfigurationManager，提前校验会让集成测试宿主误判为空密钥
            ProductionConfigurationValidator.Validate(app.Configuration, app.Environment);

            // 启动期一致性校验（P2-B4）：扫描控制器 / Action 的 [RequirePermission] 声明，与集中注册表比对；
            // 存在未声明权限即阻止启动（新增权限必须先在 PermissionDefinitions 集中声明）
            PermissionsStartupValidator.Validate(typeof(Program).Assembly);

            // 启动期一致性校验（写命令验证器覆盖）：ValidationBehavior 对无验证器的请求直接放行，
            // 故新增写命令必须同时补 IValidator<T>，缺失即在启动前汇总报错而不是静默绕过校验
            WriteCommandValidatorStartupValidator.Validate(
                typeof(Application.Contracts.Abstractions.ICommand).Assembly,
                typeof(Application.Validators.Common.WriteCommandValidator<,>).Assembly);

            // 启动期一致性校验（T5 #12）：全部错误码都须在 zh-CN / en-US 两份本地化资源中有非空文案，
            // 且不得出现无对应常量的多余键；缺失即在启动前汇总报错，避免 en-US 请求静默回退到中文文案
            ErrorMessageResourceValidator.Validate();

            // 启动期一致性校验（P0-5 命令级鉴权）：全部写命令必须显式声明权限策略，
            // 且 [RequireCommandPermission] 的权限标识须在集中注册表声明；
            // 与端点级反向扫描构成双保险——内部 ISender.Send / 非 HTTP 入口同样受命令级保护，
            // 漏标命令在运行期默认放行，故必须在此 fail-closed 阻止宿主启动。
            CommandPermissionStartupValidator.Validate(typeof(Application.Contracts.Abstractions.ICommand).Assembly);

            // 反向代理转发头解析（P0-S5）：部署在 Nginx / SLB 之后时（Forwarded:Enabled=true）解析 X-Forwarded-*，
            // 还原真实客户端 IP 供限流分区与操作日志使用；须先于任何消费 RemoteIpAddress 的中间件挂载。
            // 未启用时为空操作；仅可在可信代理后开启，KnownProxies 白名单外的转发头一律忽略
            app.UseApiForwardedHeaders();

            // TraceId 中间件（P2-B3）：紧随转发头解析之后注册（转发头才是管道最外层），生成请求链路 TraceId（响应头 X-Trace-Id），
            // 供慢请求 / SQL / 事件投递日志关联；位于全局异常处理中间件外层，保证异常路径同样带链路标识
            app.UseMiddleware<TraceIdMiddleware>();

            // 请求本地化中间件（须先于异常中间件：CurrentUICulture 按请求文化设置后，业务异常消息才能按文化翻译）
            app.UseRequestLocalization();

            // 请求日志（结构化记录方法、路径、状态码与耗时）：必须位于异常中间件「外层」，
            // 异常路径由内层异常中间件写成 500 后，日志收尾读到的才是真实状态码；
            // 放在内层时异常沿栈上抛，finally 读到的仍是默认 200，监控会漏报 5xx。
            app.UseSerilogRequestLogging();

            // 全局异常处理中间件：并非管道最外层（外层为转发头 / TraceId / 请求本地化 / 请求日志），捕获其内层（CORS / HSTS / 限流 / 鉴权 / 端点）全部异常并写统一 Result。
            // 位置保持在 CORS / HSTS 外层，但 UnifiedResponseWriter 会保留跨切响应头白名单，
            // 因此错误响应仍带 Access-Control-Allow-Origin / Strict-Transport-Security。
            app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
            // 请求体大小上限（P0-S3）：Content-Length 超过 FileStorage:MaxSizeMB+1MB 直接 413，
            // 与 Kestrel 线级上限（ConfigureKestrel）对齐，测试宿主（TestServer）行为一致
            app.UseMiddleware<RequestBodyLimitMiddleware>();

            // HTTPS 安全传输：HSTS（仅非 Development 且 HstsEnabled 开启时）+ HTTP 强制重定向（RedirectEnabled 开启时）
            app.UseApiHttps();

            // CORS 跨域中间件（须在鉴权之前，正确处理预检 OPTIONS 请求；CorsOption.Enabled=false 时不挂载）
            app.UseApiCors();

            // 接口限流中间件（须在鉴权之前，避免匿名端点被无限请求打满；RateLimitOption.Enabled=false 时不挂载）
            app.UseApiRateLimiter();

            // 租户解析中间件（MultiTenancy:Enabled=true 时读取 X-Tenant-Id 请求头，需在鉴权之前注册）
            app.UseMiddleware<TenantResolutionMiddleware>();

            // 认证与授权中间件（顺序不可颠倒）
            app.UseAuthentication();
            // 租户授权（P0-S1）：多租户开启时，认证后收敛并校验请求租户（须在 UseAuthentication 之后取 JWT 声明）
            app.UseMiddleware<TenantAuthorizationMiddleware>();
            // 强制改密拦截（P0-S4）：认证后拦截待改密用户的受保护端点访问（白名单端点放行）
            app.UseMiddleware<MustChangePasswordMiddleware>();
            app.UseAuthorization();

            // Swagger 文档管道（仅开发环境：/openapi/v1.json + /swagger UI）
            app.UseSwaggerDocumentation();

            // 启动时按「库 / 结构 / 种子」三层分别判定：库与结构由 DbInitializer 判定（结构就绪即跳过任何 DDL），
            // 种子由 SeedDataInitializer 幂等补齐；三层结论写入启动日志与 /health/ready
            using (var scope = app.Services.CreateScope())
            {
                var initializationState = scope.ServiceProvider.GetRequiredService<DatabaseInitializationState>();
                var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                await dbInitializer.InitializeAsync();

                // QRTZ_ 表在「结构就绪后、任务装载前」初始化：下方 jobScheduleManager.InitializeAsync
                // 会解析调度器并校验 QRTZ_ 表，而 IHostedService 要等 app.Run() 才启动，故显式调用一次。
                // QuartzSchemaInitializer 是静态唯一入口（已不再注册 HostedService），DDL 全部
                // CREATE TABLE IF NOT EXISTS，多副本并发冷启动不会因 check-then-create 竞态失败。
                var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOption>>();
                await QuartzSchemaInitializer.EnsureSchemaAsync(databaseOptions.Value.ConnectionString);

                var seedDataInitializer = scope.ServiceProvider.GetRequiredService<SeedDataInitializer>();
                await seedDataInitializer.InitializeAsync();

                // 启动期校验内置锚点（超管角色唯一 / admin 存在且启用 / 绑定关系存在），失败即阻止启动
                await seedDataInitializer.ValidateBuiltInAnchorsAsync();
                initializationState.MarkSeed(DatabaseInitializationStage.Ready);

                var summary = initializationState.Snapshot();
                app.Logger.LogInformation(
                    "数据库初始化完成：库={Database} 结构={Schema} 种子={Seed}",
                    summary.Database, summary.Schema, summary.Seed);

                // 装载运行中（Status=1）的定时任务进 Quartz 调度器（需在种子数据之后执行）
                var jobScheduleManager = scope.ServiceProvider.GetRequiredService<IJobScheduleManager>();
                await jobScheduleManager.InitializeAsync();
            }

            app.MapControllers();

            // 健康检查端点（匿名访问，供负载均衡 / 运维探针调用，JSON 报告便于监控采集）：
            // /health/live 仅进程存活自检；/health/ready 含数据库 / 缓存 / 调度器就绪探测
            // FallbackPolicy 要求所有端点默认认证，健康检查作为匿名探针显式放行
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = HealthReportWriter.WriteAsync
            }).AllowAnonymous();
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = HealthReportWriter.WriteAsync
            }).AllowAnonymous();

            // 启动期端点策略校验（评审 A3）：现存 minimal API 仅健康检查与 OpenAPI 文档，均显式匿名；
            // 若有人新增受保护的 minimal API（只受 FallbackPolicy 认证，不受权限过滤器 / 强制改密拦截），
            // 在此 fail-closed 阻止启动，提示改用 MVC 控制器并标注 [RequirePermission]。
            MinimalApiEndpointPolicyValidator.Validate(
                app.Services.GetRequiredService<EndpointDataSource>());

            app.Run();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Super Admin API 启动失败");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
