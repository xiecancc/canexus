using System.Diagnostics;
using Application.Behaviors;
using Application.Contracts.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Application.Tests.Behaviors;

/// <summary>用于测试的慢请求（写命令，走完整行为管道语义）。</summary>
public sealed record SlowObservabilityRequest : Request<bool>;

/// <summary>记录日志条目的假日志器（仅断言行为输出，不 mock 业务组件）。</summary>
/// <typeparam name="TCategory">日志类别。</typeparam>
public sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>已记录的日志条目（formatter 输出）。</summary>
    public List<string> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(formatter(state, exception));
    }
}

/// <summary>性能监控行为测试（P2-B3）：慢请求告警日志必须携带当前 TraceId，支撑经 TraceId 定位 SQL/事件链路。</summary>
public class PerformanceBehaviorTests
{
    /// <summary>核心断言：超过阈值（500ms）的请求输出告警日志，且日志携带当前 Activity 的 TraceId。</summary>
    [Test]
    public async Task Handle_SlowRequest_LogsWarningWithTraceId()
    {
        // ActivitySource 须有监听器才会实际创建 Activity（与生产中间件注册监听器的行为一致）
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "application.tests.observability",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("application.tests.observability");
        using var activity = source.StartActivity("slow-request");
        Assert.That(activity, Is.Not.Null, "注册监听器后应能创建 Activity");

        var logger = new RecordingLogger<PerformanceBehavior<SlowObservabilityRequest, bool>>();
        var behavior = new PerformanceBehavior<SlowObservabilityRequest, bool>(logger);
        var next = new RequestHandlerDelegate<bool>(async () =>
        {
            await Task.Delay(600);
            return true;
        });

        var response = await behavior.Handle(new SlowObservabilityRequest(), next, CancellationToken.None);

        Assert.That(response, Is.True);
        var entry = logger.Entries.SingleOrDefault(entry => entry.Contains("慢请求"));
        Assert.That(entry, Is.Not.Null, "超过阈值的请求应输出慢请求告警日志");
        Assert.That(entry!, Does.Contain(activity!.TraceId.ToString()), "慢请求日志应携带当前 TraceId（一键定位 SQL/事件链路）");
    }

    /// <summary>验证未超过阈值的请求不输出慢请求日志（避免噪音）。</summary>
    [Test]
    public async Task Handle_FastRequest_DoesNotLogSlowWarning()
    {
        var logger = new RecordingLogger<PerformanceBehavior<SlowObservabilityRequest, bool>>();
        var behavior = new PerformanceBehavior<SlowObservabilityRequest, bool>(logger);
        var next = new RequestHandlerDelegate<bool>(() => Task.FromResult(true));

        await behavior.Handle(new SlowObservabilityRequest(), next, CancellationToken.None);

        Assert.That(logger.Entries, Is.Empty, "未超过阈值的请求不应输出慢请求日志");
    }
}
