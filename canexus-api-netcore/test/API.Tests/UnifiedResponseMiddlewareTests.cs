using System.Text.Json;
using API.Middlewares;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Tests;

/// <summary>
/// 全局异常中间件单测：全部错误响应经统一写入器输出完整 Result 结构与 X-Trace-Id；
/// 客户端断开导致的取消不得误报 500 或写出响应体。
/// </summary>
[TestFixture]
public class UnifiedResponseMiddlewareTests
{
    /// <summary>业务异常映射为统一 {code,message} 结构（Result.Fail 无数据载荷），并附 X-Trace-Id 与 JSON Content-Type。</summary>
    [Test]
    public async Task BusinessException_WritesUnifiedResultWithTraceId()
    {
        var context = CreateContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new BusinessException(ErrorCodes.CommonOperationFailed, "业务失败"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        var body = ReadBody(context);
        using var document = JsonDocument.Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(context.Response.ContentType, Is.EqualTo("application/json;charset=utf-8"));
            Assert.That(context.Response.Headers["X-Trace-Id"].ToString(), Is.EqualTo("trace-123"));
            Assert.That(document.RootElement.TryGetProperty("code", out var code), Is.True);
            Assert.That(code.GetInt32(), Is.EqualTo(ErrorCodes.CommonOperationFailed));
            Assert.That(document.RootElement.TryGetProperty("message", out _), Is.True);
            Assert.That(document.RootElement.TryGetProperty("data", out _), Is.False);
        });
    }

    /// <summary>未知异常固定为 500 + 通用文案，同样附 X-Trace-Id。</summary>
    [Test]
    public async Task UnknownException_Writes500WithoutStackDetails()
    {
        var context = CreateContext();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("内部堆栈细节"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        var body = ReadBody(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(context.Response.Headers["X-Trace-Id"].ToString(), Is.EqualTo("trace-123"));
            Assert.That(body, Does.Not.Contain("内部堆栈细节"));
            Assert.That(body, Does.Contain("服务器内部错误"));
        });
    }

    /// <summary>客户端断开触发的取消：不写响应、不计 500。</summary>
    [Test]
    public async Task ClientDisconnect_DoesNotWrite500Response()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext();
        context.RequestAborted = cts.Token;
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Body.Length, Is.EqualTo(0));
        });
    }

    /// <summary>构造带可回读响应体与固定 TraceIdentifier 的 HTTP 上下文。</summary>
    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-123"
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>回读响应体字符串。</summary>
    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
