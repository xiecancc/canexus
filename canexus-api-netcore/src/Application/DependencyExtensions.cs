
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
    /// <summary>注册应用层能力：MediatR（命令 / 查询请求分发 + 领域事件通知）、Pipeline Behaviors（审计 / 验证 / 数据范围守卫 / 日志 / 性能 / 事务）、FluentValidation 验证器、AutoMapper 映射、领域事件发布器、权限校验与数据范围过滤。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // MediatR：扫描本程序集注册全部命令 / 查询处理器（Commands / Queries 目录）
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());

            // Pipeline Behaviors：默认审计在最外层（含校验失败与事务回滚后的失败路径；[OperLog] 端点由
            // OperLogActionFilter 接管去重），验证在最前（失败抛 ValidationException，不开启事务），
            // 随后用户写命令数据范围守卫（取得目标实体后越权即 403，不进入处理器与事务），
            // 其后日志与性能监控，事务管道最内层（仅包裹处理器执行，写命令自动开启事务）
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UserDataScopeBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            configuration.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
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

        // 数据范围过滤：依赖仓储与当前用户上下文，按请求解析并缓存
        services.AddScoped<IDataScopeFilter, DataScopeFilter>();

        return services;
    }
}
