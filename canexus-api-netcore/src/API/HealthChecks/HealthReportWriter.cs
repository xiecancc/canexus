using Domain.Shared.Json;
using Infrastructure.Shared.DataAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.HealthChecks;
/// <summary>
/// 健康报告 JSON 响应写出器：供负载均衡与监控采集解析。
/// <para>两个健康端点均匿名可达，因此只输出总体状态、耗时与「匿名化」检查项状态：
/// 不输出检查项的 description（可能含连接串 / 异常堆栈），也不输出内部依赖名称
/// （mysql / cache / quartz 等），改用按名称稳定排序后的通用序号键。</para>
/// </summary>
public static class HealthReportWriter
{
    /// <summary>写出脱敏后的健康报告 JSON（{status, totalDurationMs, entries:{check-N:{status,durationMs}}}）。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="report">健康报告。</param>
    /// <returns>表示异步操作的任务。</returns>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json;charset=utf-8";

        var entries = report.Entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select((entry, index) => (Key: $"check-{index + 1}", Entry: entry.Value))
            .ToDictionary(
                item => item.Key,
                item => new
                {
                    status = item.Entry.Status.ToString(),
                    durationMs = (int)item.Entry.Duration.TotalMilliseconds
                });

        // 数据库初始化三层结论：只输出阶段状态与版本号，不输出表名 / 连接串 / 异常文本
        var snapshot = context.RequestServices?.GetService<DatabaseInitializationState>()?.Snapshot();
        object? initialization = snapshot is null
            ? null
            : new
            {
                database = snapshot.Database,
                schema = snapshot.Schema,
                seed = snapshot.Seed,
                appliedVersion = snapshot.AppliedVersion,
                expectedVersion = snapshot.ExpectedVersion
            };

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            entries,
            initialization
        };

        return context.Response.WriteAsync(JsonHelper.Serialize(payload));
    }
}
