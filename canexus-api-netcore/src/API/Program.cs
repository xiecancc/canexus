
using API.Extensions;
using API.Filters;
using API.HealthChecks;
using API.Middlewares;
using Application;
using Domain.Services;
using Domain.Shared.Results;
using Infrastructure;
using Infrastructure.DataAccess;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
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

            // 配置选项：启动时统一注册全部 Option（继承 OptionBase，按类名约定映射配置节；
            // 绑定 + 校验 + 热加载 + 启动即验），不依赖任何业务注册，须先于业务模块执行
            builder.Services.RegisterOptions(builder.Configuration);

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

            // 定时任务调度：Quartz 调度器与 IJobScheduleManager（基础设施层实现）
            builder.Services.AddScheduling();

            // 健康检查：liveness / readiness 检查项（MySQL / 分布式缓存 / Quartz），经 MapHealthChecks 暴露
            builder.Services.AddApiHealthChecks();

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

                    var result = Result.Fail(ErrorCodes.CommonValidationFailed, string.Join("；", messages));
                    return new BadRequestObjectResult(result);
                };
            });

            var app = builder.Build();

            // 全局异常处理中间件最先注册，确保后续管道异常均被统一捕获
            app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

            // 请求日志（结构化记录方法、路径、状态码与耗时）
            app.UseSerilogRequestLogging();

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
            app.UseAuthorization();

            // Swagger 文档管道（仅开发环境：/openapi/v1.json + /swagger UI）
            app.UseSwaggerDocumentation();

            // 启动时初始化数据库：自动建库 + CodeFirst 同步表结构 + 幂等种子数据
            using (var scope = app.Services.CreateScope())
            {
                var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                await dbInitializer.InitializeAsync();

                var seedDataInitializer = scope.ServiceProvider.GetRequiredService<SeedDataInitializer>();
                await seedDataInitializer.InitializeAsync();

                // 装载运行中（Status=1）的定时任务进 Quartz 调度器（需在种子数据之后执行）
                var jobScheduleManager = scope.ServiceProvider.GetRequiredService<IJobScheduleManager>();
                await jobScheduleManager.InitializeAsync();
            }

            app.MapControllers();

            // 健康检查端点（匿名访问，供负载均衡 / 容器编排探针调用，JSON 报告便于监控采集）：
            // /health/live 仅进程存活自检；/health/ready 含数据库 / 缓存 / 调度器就绪探测
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = HealthReportWriter.WriteAsync
            });
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = HealthReportWriter.WriteAsync
            });

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
