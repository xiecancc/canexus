
using Application.Behaviors;
using Application.Permissions;
using Application.Scopes;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Application;
/// <summary>应用层依赖注册扩展。</summary>
public static class DependencyExtensions
{
    /// <summary>注册应用层能力：MediatR（CQRS 分发）、Pipeline Behaviors（验证 / 日志 / 性能）、FluentValidation 验证器、AutoMapper 映射、权限校验与数据范围过滤。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // MediatR：扫描本程序集注册全部命令 / 查询处理器（Commands / Queries 目录）
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyExtensions).Assembly);
        });

        // Pipeline Behaviors：验证在最前（失败抛 ValidationException），其后日志与性能监控
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

        // FluentValidation：扫描本程序集注册全部验证器（Validators 目录），按请求类型注入验证管道
        services.AddValidatorsFromAssembly(typeof(DependencyExtensions).Assembly);

        // AutoMapper：扫描本程序集 Profile 映射配置（Mappings 目录）
        services.AddAutoMapper(expression => expression.AddMaps(typeof(DependencyExtensions).Assembly));

        services.AddScoped<IPermissionChecker, PermissionChecker>();

        // 数据范围过滤：依赖仓储与当前用户上下文，按请求解析并缓存
        services.AddScoped<IDataScopeFilter, DataScopeFilter>();

        return services;
    }
}
