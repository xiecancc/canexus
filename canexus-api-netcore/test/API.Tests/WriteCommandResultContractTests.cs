
using System.Text.Json;
using API.Controllers;
using Application.Contracts.Configs;
using Domain.Shared.Json;
using Domain.Shared.Results;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests;
/// <summary>
/// 写命令响应契约单测（T-16）：聚合写命令的 bool 结果必须进入 Result&lt;bool&gt;.Data，
/// 恢复命令的幂等 false 语义不得在 HTTP 层丢失；无返回值命令仍输出无 data 字段的统一响应。
/// </summary>
[TestFixture]
public class WriteCommandResultContractTests
{
    /// <summary>创建端点把写命令 bool 原样写入 data。</summary>
    /// <param name="affected">写命令执行结果。</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task Create_PropagatesCommandBoolIntoData(bool affected)
    {
        var controller = new SysConfigController(new BoolSender(affected));

        var action = await controller.Create(new ConfigInputRequest());
        var value = ((OkObjectResult)action).Value!;

        Assert.That(value, Is.TypeOf<Result<bool>>());
        using var document = JsonDocument.Parse(JsonHelper.Serialize(value, value.GetType()));
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(Result.SuccessCode));
            Assert.That(document.RootElement.GetProperty("data").GetBoolean(), Is.EqualTo(affected));
            Assert.That(document.RootElement.GetProperty("message").GetString(), Is.EqualTo("创建成功"));
        });
    }

    /// <summary>恢复端点在幂等未生效时仍返回成功码，但 data=false 并给出对应文案。</summary>
    /// <param name="restored">恢复命令执行结果。</param>
    /// <param name="expectedMessage">期望提示消息。</param>
    [TestCase(true, "恢复成功")]
    [TestCase(false, "参数不存在或未删除，无需恢复")]
    public async Task Restore_PropagatesIdempotentFalseIntoData(bool restored, string expectedMessage)
    {
        var controller = new SysConfigController(new BoolSender(restored));

        var action = await controller.Restore(Guid.NewGuid(), Guid.NewGuid());
        var value = ((OkObjectResult)action).Value!;

        Assert.That(value, Is.TypeOf<Result<bool>>());
        using var document = JsonDocument.Parse(JsonHelper.Serialize(value, value.GetType()));
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("data").GetBoolean(), Is.EqualTo(restored));
            Assert.That(document.RootElement.GetProperty("message").GetString(), Is.EqualTo(expectedMessage));
        });
    }

    /// <summary>无返回值写命令（Request / Unit）仍使用 OkMessage，响应体不含 data 字段。</summary>
    [Test]
    public async Task Clean_UnitCommand_UsesOkMessageWithoutDataField()
    {
        var controller = new SysOperLogController(new BoolSender(true));

        var action = await controller.Clean(DateTime.Today.AddDays(-1), DateTime.Today, true);
        var value = ((OkObjectResult)action).Value!;

        var json = JsonHelper.Serialize(value, value.GetType());
        Assert.Multiple(() =>
        {
            Assert.That(value.GetType(), Is.EqualTo(typeof(Result)));
            Assert.That(json, Does.Contain("\"message\":\"清空成功\""));
            Assert.That(json, Does.Not.Contain("\"data\""));
        });
    }

    /// <summary>可控 bool 返回值的 ISender 桩：不触碰数据库，仅验证控制器是否消费 Send 的结果。</summary>
    private sealed class BoolSender(bool result) : ISender
    {
        /// <inheritdoc />
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return typeof(TResponse) == typeof(bool)
                ? Task.FromResult((TResponse)(object)result)
                : Task.FromResult(default(TResponse)!);
        }

        /// <inheritdoc />
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        /// <inheritdoc />
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
