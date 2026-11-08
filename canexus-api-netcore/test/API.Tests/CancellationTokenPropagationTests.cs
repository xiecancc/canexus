using API.Controllers;
using Application.Contracts.Queries;
using MediatR;

namespace API.Tests;

/// <summary>
/// 控制器取消令牌透传单测：动作收到的 CancellationToken 必须原样传入 ISender.Send
/// （覆盖带返回值与无返回值两种 MediatR Send 重载），否则客户端断开后管道仍会跑完整条链路。
/// </summary>
[TestFixture]
public class CancellationTokenPropagationTests
{
    /// <summary>带返回值查询端点把令牌传入 Send&lt;TResponse&gt;。</summary>
    [Test]
    public async Task Controller_PassesToken_ToGenericSend()
    {
        using var cts = new CancellationTokenSource();
        var sender = new RecordingSender();
        var controller = new SysConfigController(sender);

        await controller.GetByKey("sys.user.initPassword", cts.Token);

        Assert.That(sender.Tokens, Is.EqualTo(new[] { cts.Token }));
    }

    /// <summary>写命令端点（AggregateDeleteCommand : Request&lt;bool&gt;）同样把令牌传入 Send。</summary>
    [Test]
    public async Task Controller_PassesToken_ToUnitSend()
    {
        using var cts = new CancellationTokenSource();
        var sender = new RecordingSender();
        var controller = new SysLoginLogController(sender);

        await controller.Delete(Guid.NewGuid(), Guid.NewGuid(), cts.Token);

        Assert.That(sender.Tokens, Is.EqualTo(new[] { cts.Token }));
    }

    /// <summary>记录每次 Send 收到的取消令牌的 ISender 桩。</summary>
    private sealed class RecordingSender : ISender
    {
        /// <summary>按调用顺序记录的令牌。</summary>
        public List<CancellationToken> Tokens { get; } = [];

        /// <inheritdoc />
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            if (request is ConfigQueryByKey)
            {
                return Task.FromResult((TResponse)(object)"test-value");
            }

            // 其余请求（写命令 Request<bool> / 无返回值 Request<Unit>）返回类型默认值：
            // 控制器不消费该结果，本测试只断言取消令牌是否原样透传
            return Task.FromResult(default(TResponse)!);
        }

        /// <inheritdoc />
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

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
