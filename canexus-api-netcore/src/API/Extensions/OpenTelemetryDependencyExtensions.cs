
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace API.Extensions;
/// <summary>OpenTelemetry 可观测性配置选项。</summary>
public sealed class OtelOption
{
    /// <summary>是否启用 OTel 采集与导出（默认关闭，经 Otel:Enabled 或环境变量开启）。</summary>
    public bool Enabled { get; set; }

    /// <summary>OTLP 导出端点（如 http://otel-collector:4317）；未配置时仅采集不导出。</summary>
    public string? OtlpEndpoint { get; set; }
}

/// <summary>OpenTelemetry 可观测性注册扩展：Trace（ASP.NET Core 入站 / HttpClient 出站）+ Metrics（运行时指标），经 OTLP 导出。</summary>
public static class OpenTelemetryDependencyExtensions
{
    /// <summary>服务名（OTel Resource 与 Serilog SourceContext 一致，便于后端按服务聚合）。</summary>
    private const string ServiceName = "canexus-api";

    /// <summary>注册 OpenTelemetry：默认关闭（Otel:Enabled），开启后采集 Trace 与 Metrics 并经 OTLP 导出；
    /// 未配置 OtlpEndpoint 时仅采集（供本地调试器附加）不外发。导出失败不影响业务（SDK 内部容错）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddOpenTelemetrySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var option = configuration.GetSection("Otel").Get<OtelOption>() ?? new OtelOption();
        services.AddSingleton(option);

        if (!option.Enabled)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter =>
                {
                    if (!string.IsNullOrWhiteSpace(option.OtlpEndpoint))
                    {
                        exporter.Endpoint = new Uri(option.OtlpEndpoint);
                    }
                }))
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter =>
                {
                    if (!string.IsNullOrWhiteSpace(option.OtlpEndpoint))
                    {
                        exporter.Endpoint = new Uri(option.OtlpEndpoint);
                    }
                }));

        return services;
    }
}
