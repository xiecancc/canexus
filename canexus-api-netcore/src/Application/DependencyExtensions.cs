
using System.Reflection;
using Application.Behaviors;
using Application.Contracts.Configs;
using Application.Events;
using Application.Permissions;
using Application.Scopes;
using Domain.Services.Events;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Application;
/// <summary>应用层依赖注册扩展。</summary>
public static class DependencyExtensions
{
    /// <summary>注册应用层能力：MediatR（命令 / 查询请求分发 + 领域事件通知）、Pipeline Behaviors（日志 / 性能监控置于最外层覆盖全部短路请求，其后幂等 / 审计 / 验证 / 数据范围守卫 / 事务）、FluentValidation 验证器、AutoMapper 映射、领域事件发布器、权限校验与数据范围过滤。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // MediatR：扫描本程序集注册全部命令 / 查询处理器（Commands / Queries 目录）
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());

            // Pipeline Behaviors（洋葱模型：注册顺序即由外到内，先注册者包裹后注册者，短路请求必经最外层）：
            // 日志 / 性能监控置于最外层——「记录每个请求」须覆盖三类短路请求：
            //   校验失败（Validation 抛 ValidationException）、越权拒绝（Permission 短路）、幂等命中（Idempotency 直接返回）。
            // 幂等命中在 Idempotency 处即返回、不调用 next()，二者只有位于其外层才不会被跳过；
            // Logging 在入口记录开始、以 try/catch 记录异常，Performance 统计含幂等命中在内的请求耗时，
            // 异常短路路径由 Logging 的异常日志兜底覆盖。
            // Idempotency 命中仍只返回缓存结果，不重复执行审计 / 校验 / 事务（这些均在其内层）；
            // 其后审计（含校验失败与事务回滚后的失败路径；[OperLog] 端点由 OperLogActionFilter 接管去重），
            // 命令级鉴权（P0-5：覆盖内部 ISender.Send / 非 HTTP 入口，端点特性降级为增强；
            // 置于审计之后使越权拒绝也落审计、置于验证之前使未授权请求不做业务校验），
            // 验证（失败抛 ValidationException，不开启事务），用户写命令数据范围守卫
            //（取得目标实体后越权即 403，不进入处理器与事务），
            // 事务管道最内层（仅包裹处理器执行，写命令自动开启事务）
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UserDataScopeBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

            // 通知发布策略：逐监听器隔离（单个监听器失败不阻断其余监听器），但失败须以聚合异常向上
            // 传播给 Outbox 投递服务进入重试，绝不把消息标记为已处理（见 IsolatedNotificationPublisher）
            configuration.NotificationPublisherType = typeof(IsolatedNotificationPublisher);
        });

        // AutoMapper：扫描本程序集 Profile 映射配置（Mappings 目录）
        services.AddAutoMapper(config =>
        {
            config.AddMaps(Assembly.GetExecutingAssembly());
        });

        // FluentValidation：扫描本程序集注册全部验证器（Validators 目录），按请求类型注入验证管道
        services.AddValidatorsFromAssembly(typeof(DependencyExtensions).Assembly);

        // 领域事件发布器（P1-A2）：事务 Outbox——事件随业务事务持久化，提交后同步投递，失败由后台 worker 重试
        services.AddScoped<IDomainEventPublisher, OutboxDomainEventPublisher>();
        services.AddScoped<PendingOutboxDelivery>();
        services.AddScoped<IOutboxDeliveryService, OutboxDeliveryService>();

        services.AddScoped<IPermissionChecker, PermissionChecker>();

        // 富文本 HTML 净化（防存储型 XSS）：白名单净化器单例复用，公告等富文本字段入库前净化
        services.AddSingleton<Security.HtmlRichTextSanitizer>(_ =>
            new Security.HtmlRichTextSanitizer(new Ganss.Xss.HtmlSanitizer()));

        // 数据范围过滤：依赖仓储与当前用户上下文，按请求解析并缓存
        services.AddScoped<IDataScopeFilter, DataScopeFilter>();

        return services;
    }
}
