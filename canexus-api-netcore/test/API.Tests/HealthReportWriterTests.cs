using System.Text.Json;
using API.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.Tests;

/// <summary>健康报告写出器单测：匿名端点不得输出检查项描述、异常细节与内部依赖名称。</summary>
[TestFixture]
public class HealthReportWriterTests
{
    /// <summary>脱敏后只保留总体状态与匿名检查项状态，结构仍可被监控解析。</summary>
    [Test]
    public async Task WriteAsync_OmitsDescriptionsAndInternalDependencyNames()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["mysql"] = new HealthReportEntry(
                HealthStatus.Healthy, "数据库连接正常 10.0.0.1:3306", TimeSpan.FromMilliseconds(3), null, null),
            ["quartz"] = new HealthReportEntry(
                HealthStatus.Unhealthy, "Quartz 调度器未启动", TimeSpan.FromMilliseconds(1),
                new InvalidOperationException("敏感异常细节"), null)
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(4));

        await HealthReportWriter.WriteAsync(context, report);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
            Assert.That(document.RootElement.TryGetProperty("entries", out var entriesElement), Is.True);
            Assert.That(entriesElement.EnumerateObject().Count(), Is.EqualTo(2));
            Assert.That(body, Does.Not.Contain("description"));
            Assert.That(body, Does.Not.Contain("mysql"));
            Assert.That(body, Does.Not.Contain("quartz"));
            Assert.That(body, Does.Not.Contain("10.0.0.1"));
            Assert.That(body, Does.Not.Contain("敏感异常细节"));
        });

        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateObject())
        {
            Assert.That(entry.Name, Does.Match(@"^check-\d+$"));
            Assert.That(entry.Value.TryGetProperty("status", out _), Is.True);
            Assert.That(entry.Value.TryGetProperty("durationMs", out _), Is.True);
        }
    }
}
