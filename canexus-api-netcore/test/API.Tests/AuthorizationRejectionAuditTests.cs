using System.Reflection;
using API.Authorization;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Http;

namespace API.Tests;
/// <summary>
/// 拒绝审计辅助方法单测（P1-1）：审计写入抛异常不得影响调用方（拒绝响应），
/// 拒绝原因须脱敏后如实落库（失败状态 / 业务类型「其他」/ 真实请求方法与 URL / 错误码）。
/// </summary>
[TestFixture]
public class AuthorizationRejectionAuditTests
{
    /// <summary>日志库写入抛异常时 WriteAsync 不得向调用方抛出（审计失败不影响拒绝响应）。</summary>
    [Test]
    public void WriteAsync_InsertThrows_DoesNotPropagate()
    {
        var (context, recorder) = CreateContext();
        recorder.ThrowOnInsert = true;

        Assert.DoesNotThrowAsync(async () =>
            await AuthorizationRejectionAudit.WriteAsync(context, "无权限访问", ErrorCodes.AuthForbidden));
    }

    /// <summary>拒绝原因脱敏后写入失败状态日志，并保留错误码、请求方法与真实 URL。</summary>
    [Test]
    public async Task WriteAsync_RecordsMaskedReasonAndRequestMetadata()
    {
        var (context, recorder) = CreateContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/system/users";
        context.Request.QueryString = new QueryString("?keyword=abc");

        await AuthorizationRejectionAudit.WriteAsync(context, "越权尝试 password=PlainSecret", ErrorCodes.AuthForbidden);

        Assert.Multiple(() =>
        {
            Assert.That(recorder.Logs.Count, Is.EqualTo(1), "须写出一条拒绝审计");
            var log = recorder.Logs[0];
            Assert.That(log.Status, Is.EqualTo(ExecutionStatus.Failure), "拒绝审计须为失败状态");
            Assert.That(log.BusinessType, Is.EqualTo(OperBusinessType.None), "拒绝归为「其他」业务类型");
            Assert.That(log.RequestMethod, Is.EqualTo("POST"), "须记录真实请求方法");
            Assert.That(log.RequestUrl, Does.Contain("/api/system/users"), "须记录真实请求 URL");
            Assert.That(log.ErrorMsg, Does.Contain(ErrorCodes.AuthForbidden.ToString()), "错误码须可检索");
            Assert.That(log.ErrorMsg, Does.Contain("越权尝试"), "拒绝原因须记录");
            Assert.That(log.ErrorMsg, Does.Not.Contain("PlainSecret"), "原因中的敏感值须脱敏");
        });
    }

    /// <summary>请求服务不可用（RequestServices 未注册仓储）时不得抛异常。</summary>
    [Test]
    public void WriteAsync_ServiceUnavailable_DoesNotPropagate()
    {
        var context = new DefaultHttpContext();

        Assert.DoesNotThrowAsync(async () =>
            await AuthorizationRejectionAudit.WriteAsync(context, "跨租户访问被拒绝", ErrorCodes.TenantForbidden));
    }

    /// <summary>构造带 IOperLogRepository 桩的 HTTP 上下文；其余可选服务故意不注册以验证健壮性。</summary>
    /// <returns>HTTP 上下文与操作日志记录器。</returns>
    private static (DefaultHttpContext Context, OperationLogRecorder Recorder) CreateContext()
    {
        var proxy = DispatchProxy.Create<IOperLogRepository, OperationLogRecorder>();
        var recorder = (OperationLogRecorder)proxy;
        var context = new DefaultHttpContext { RequestServices = new StubServiceProvider(proxy) };
        return (context, recorder);
    }

    /// <summary>最小 IServiceProvider 桩：仅解析 IOperLogRepository，其余返回 null。</summary>
    private sealed class StubServiceProvider(IOperLogRepository repository) : IServiceProvider
    {
        /// <inheritdoc />
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IOperLogRepository) ? repository : null;
    }
}
