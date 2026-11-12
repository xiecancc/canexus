using System.Reflection;
using API.Filters;
using API.Logging;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Contexts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Tests;

/// <summary>
/// 操作日志过滤器单测：next() 抛出的业务异常 / 取消异常必须原样传播到全局异常中间件，
/// 日志库写入失败不得顶替业务响应；失败状态须如实落库。
/// </summary>
[TestFixture]
public class OperLogActionFilterTests
{
    /// <summary>next() 直接抛出的业务异常不得被 catch 吞掉。</summary>
    [Test]
    public void Filter_BusinessExceptionFromNext_Propagates()
    {
        var (filter, context, _) = CreateFilter(out var recorder);
        var expected = new BusinessException(ErrorCodes.CommonOperationFailed, "业务失败");
        ActionExecutionDelegate throwing = () => throw expected;

        var thrown = Assert.ThrowsAsync<BusinessException>(async () =>
            await filter.OnActionExecutionAsync(context, throwing));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(expected));
            Assert.That(recorder.Logs.Count, Is.EqualTo(1));
            Assert.That(recorder.Logs[0].Status, Is.EqualTo(ExecutionStatus.Failure));
            Assert.That(recorder.Logs[0].ErrorMsg, Does.Contain("业务失败"));
        });
    }

    /// <summary>next() 抛出的取消异常同样原样传播，不被日志告警吞掉。</summary>
    [Test]
    public void Filter_OperationCanceledFromNext_Propagates()
    {
        var (filter, context, _) = CreateFilter(out _);
        var expected = new OperationCanceledException("客户端断开");
        ActionExecutionDelegate throwing = () => throw expected;

        var thrown = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await filter.OnActionExecutionAsync(context, throwing));

        Assert.That(thrown, Is.SameAs(expected));
    }

    /// <summary>ActionExecutedContext.Exception 路径：记录失败日志且不吞掉异常。</summary>
    [Test]
    public async Task Filter_ExecutedContextException_RecordsFailureAndKeepsException()
    {
        var (filter, context, executed) = CreateFilter(out var recorder);
        executed.Exception = new BusinessException(ErrorCodes.CommonOperationFailed, "管道失败");
        ActionExecutionDelegate next = () => Task.FromResult(executed);

        await filter.OnActionExecutionAsync(context, next);

        Assert.Multiple(() =>
        {
            Assert.That(executed.Exception, Is.Not.Null, "过滤器不得清空 / 吞掉 ActionExecutedContext.Exception");
            Assert.That(recorder.Logs.Count, Is.EqualTo(1));
            Assert.That(recorder.Logs[0].Status, Is.EqualTo(ExecutionStatus.Failure));
        });
    }

    /// <summary>框架注入的 CancellationToken 不得进入审计参数序列化（IntPtr 不可序列化会丢整条日志）。</summary>
    [Test]
    public async Task Filter_CancellationTokenArgument_ExcludedFromOperParam()
    {
        var (filter, context, executed) = CreateFilter(out var recorder);
        ActionExecutionDelegate next = () => Task.FromResult(executed);

        await filter.OnActionExecutionAsync(context, next);

        Assert.Multiple(() =>
        {
            Assert.That(recorder.Logs.Count, Is.EqualTo(1), "含取消令牌的请求仍须写出操作日志");
            Assert.That(recorder.Logs[0].OperParam, Does.Not.Contain("cancellationToken"));
            Assert.That(recorder.Logs[0].OperParam, Does.Contain("keyword"));
        });
    }

    /// <summary>日志库不可用时业务响应不受影响（成功路径不抛异常）。</summary>
    [Test]
    public void Filter_LogInsertFails_DoesNotAffectBusinessResponse()
    {
        var (filter, context, executed) = CreateFilter(out var recorder);
        recorder.ThrowOnInsert = true;
        ActionExecutionDelegate next = () => Task.FromResult(executed);

        Assert.DoesNotThrowAsync(async () => await filter.OnActionExecutionAsync(context, next));
    }

    /// <summary>构造带 [OperLog] 端点元数据的过滤器与 Action 上下文。</summary>
    private static (OperLogActionFilter Filter, ActionExecutingContext Executing, ActionExecutedContext Executed)
        CreateFilter(out OperationLogRecorder recorder)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(new OperLogAttribute("测试模块", OperBusinessType.Add)),
            displayName: "test"));
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var controller = new object();
        // 模拟真实 MVC 动作参数：框架注入的 CancellationToken 会一并出现在 ActionArguments 中
        var actionArguments = new Dictionary<string, object?>
        {
            ["keyword"] = "audit",
            ["cancellationToken"] = CancellationToken.None
        };
        var executing = new ActionExecutingContext(actionContext, filters, actionArguments, controller);
        var executed = new ActionExecutedContext(actionContext, filters, controller);

        var proxy = DispatchProxy.Create<IOperLogRepository, OperationLogRecorder>();
        recorder = (OperationLogRecorder)proxy;
        var filter = new OperLogActionFilter(
            proxy, new EmptyUserContext(), new OperationContextStub(), NullLogger<OperLogActionFilter>.Instance);
        return (filter, executing, executed);
    }

    /// <summary>当前用户上下文空实现。</summary>
    private sealed class EmptyUserContext : IUserContext
    {
        /// <inheritdoc />
        public Guid? UserId => null;

        /// <inheritdoc />
        public string? UserName => "tester";

        /// <inheritdoc />
        public Guid? DeptId => null;

        /// <inheritdoc />
        public IReadOnlyList<Guid> RoleIds => [];

        /// <inheritdoc />
        public IReadOnlyList<string> Roles => [];

        /// <inheritdoc />
        public Guid? TenantId => null;

        /// <inheritdoc />
        public bool IsAuthenticated => true;

        /// <inheritdoc />
        public bool IsSuperAdmin => false;
    }

    /// <summary>请求上下文桩：记录接管标记。</summary>
    private sealed class OperationContextStub : IOperationContext
    {
        /// <inheritdoc />
        public string? TraceId => "trace-test";

        /// <inheritdoc />
        public string? HttpMethod => null;

        /// <inheritdoc />
        public string? RequestPath => null;

        /// <inheritdoc />
        public string? ClientIp => null;

        /// <inheritdoc />
        public bool IsOperLogHandled { get; private set; }

        /// <inheritdoc />
        public void MarkOperLogHandled() => IsOperLogHandled = true;
    }
}

/// <summary>IOperLogRepository 动态代理：记录写入的日志，可模拟日志库故障。</summary>
public class OperationLogRecorder : DispatchProxy
{
    /// <summary>模拟日志写入失败。</summary>
    public bool ThrowOnInsert { get; set; }

    /// <summary>已写入的日志。</summary>
    public List<SysOperLog> Logs { get; } = [];

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "InsertAsync" && args?[0] is SysOperLog log)
        {
            if (ThrowOnInsert)
            {
                throw new InvalidOperationException("日志库不可用");
            }

            Logs.Add(log);
            return Task.FromResult(true);
        }

        throw new NotSupportedException($"未预期的仓储调用：{targetMethod?.Name}");
    }
}
