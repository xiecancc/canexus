
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.HealthChecks;
/// <summary>健康报告 JSON 响应写出器：替代默认的纯文本输出，供负载均衡 / 容器编排与监控采集解析。</summary>
public static class HealthReportWriter
{
    /// <summary>JSON 序列化选项（camelCase，与全局 API 响应风格一致）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>写出健康报告 JSON（{status, totalDurationMs, entries:{name:{status,description,durationMs}}}）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="report">健康报告。</param>
    /// <returns>表示异步操作的任务。</returns>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json;charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = (int)entry.Value.Duration.TotalMilliseconds
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
