using Application.Events;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Shared.Enums;
using Infrastructure.Shared.Options;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Application.Tests.Events;

/// <summary>Outbox 投递服务测试用仓储桩：记录完成 / 失败状态写入，不访问数据库。</summary>
internal sealed class FakeOutboxMessageRepository : IOutboxMessageRepository
{
    /// <summary>TryClaimAsync 的返回值。</summary>
    public bool ClaimResult { get; set; } = true;

    /// <summary>MarkProcessedAsync 的返回值（模拟认领已易主）。</summary>
    public bool MarkProcessedResult { get; set; } = true;

    /// <summary>MarkProcessedAsync 调用次数。</summary>
    public int MarkProcessedCalls { get; private set; }

    /// <summary>RecordFailureAsync 调用次数。</summary>
    public int RecordFailureCalls { get; private set; }

    /// <summary>最近一次记录的失败原因。</summary>
    public string? LastError { get; private set; }

    /// <summary>最近一次记录的尝试次数。</summary>
    public int LastAttemptCount { get; private set; }

    /// <inheritdoc />
    public Task InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<List<OutboxMessage>> ClaimBatchAsync(int batchSize, string owner, TimeSpan lease, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<OutboxMessage>());

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(Guid id, string owner, TimeSpan lease, CancellationToken cancellationToken = default)
        => Task.FromResult(ClaimResult);

    /// <inheritdoc />
    public Task<bool> MarkProcessedAsync(Guid id, string owner, DateTime processedAt, CancellationToken cancellationToken = default)
    {
        MarkProcessedCalls++;
        return Task.FromResult(MarkProcessedResult);
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(Guid id, string owner, string error, int attemptCount, int maxAttempts, DateTime nextRetryAt, CancellationToken cancellationToken = default)
    {
        RecordFailureCalls++;
        LastError = error;
        LastAttemptCount = attemptCount;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> MarkStaleAsFailedAsync(DateTime createdBeforeUtc, string reason, int maxRows, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> DeleteCompletedBeforeAsync(DateTime? processedCutoffUtc, DateTime? failedCutoffUtc, int maxRows, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}

/// <summary>模拟监听器失败的发布器：任何发布都抛异常。</summary>
internal sealed class ThrowingPublisher : IPublisher
{
    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => throw new InvalidOperationException("模拟监听器故障");

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("模拟监听器故障");
}

/// <summary>
/// Outbox 投递服务状态机单元测试：监听器失败必须记录重试而非标记已处理；
/// 成功才标记已处理；认领易主时既不重复投递也不误记失败。
/// </summary>
[TestFixture]
public class OutboxDeliveryServiceTests
{
    private const string Owner = "worker-a";

    /// <summary>监听器（发布器）失败时：消息进入失败记录，不得标记为已处理。</summary>
    [Test]
    public async Task DeliverAsync_ListenerFailure_RecordsFailureAndSkipsProcessed()
    {
        var repository = new FakeOutboxMessageRepository();
        var service = CreateService(repository, new ThrowingPublisher());

        await service.DeliverAsync([CreateClaimedMessage()], Owner, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(repository.MarkProcessedCalls, Is.Zero, "监听器失败时不得标记 Processed");
            Assert.That(repository.RecordFailureCalls, Is.EqualTo(1), "监听器失败必须记录一次失败以进入重试");
            Assert.That(repository.LastAttemptCount, Is.EqualTo(1));
            Assert.That(repository.LastError, Does.Contain("模拟监听器故障"));
        });
    }

    /// <summary>发布成功时：标记已处理，且不记录失败。</summary>
    [Test]
    public async Task DeliverAsync_Success_MarksProcessedOnly()
    {
        var repository = new FakeOutboxMessageRepository();
        var service = CreateService(repository, new RecordingPublisher());

        await service.DeliverAsync([CreateClaimedMessage()], Owner, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(repository.MarkProcessedCalls, Is.EqualTo(1));
            Assert.That(repository.RecordFailureCalls, Is.Zero);
        });
    }

    /// <summary>认领易主（MarkProcessed 命中 0 行）时：不重复投递也不误记失败。</summary>
    [Test]
    public async Task DeliverAsync_StaleClaim_SkipsWithoutRecordingFailure()
    {
        var repository = new FakeOutboxMessageRepository { MarkProcessedResult = false };
        var service = CreateService(repository, new RecordingPublisher());

        await service.DeliverAsync([CreateClaimedMessage()], Owner, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(repository.MarkProcessedCalls, Is.EqualTo(1));
            Assert.That(repository.RecordFailureCalls, Is.Zero, "认领已易主由新副本负责，本轮不记失败");
        });
    }

    /// <summary>非法的事件类型不得被投递为已处理（ResolveType 白名单）。</summary>
    [Test]
    public async Task DeliverAsync_InvalidEventType_RecordsFailure()
    {
        var repository = new FakeOutboxMessageRepository();
        var service = CreateService(repository, new RecordingPublisher());
        var message = OutboxMessage.Create(typeof(string).FullName!, "\"text\"");
        message.Status = OutboxMessageStatus.Processing;
        message.ClaimOwner = Owner;

        await service.DeliverAsync([message], Owner, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(repository.MarkProcessedCalls, Is.Zero);
            Assert.That(repository.RecordFailureCalls, Is.EqualTo(1));
        });
    }

    /// <summary>构造已认领的合法领域事件消息（模拟 worker 批量认领后的入参）。</summary>
    /// <returns>Processing 状态、带 owner 的 Outbox 消息。</returns>
    private static OutboxMessage CreateClaimedMessage()
    {
        var message = OutboxMessage.Create(
            typeof(UserPasswordResetEvent).FullName!,
            OutboxEventSerializer.Serialize(new UserPasswordResetEvent(Guid.NewGuid())));
        message.Status = OutboxMessageStatus.Processing;
        message.ClaimOwner = Owner;
        return message;
    }

    /// <summary>构造投递服务（固定默认 Outbox 配置）。</summary>
    /// <param name="repository">仓储桩。</param>
    /// <param name="publisher">发布器桩。</param>
    /// <returns>投递服务。</returns>
    private static OutboxDeliveryService CreateService(IOutboxMessageRepository repository, IPublisher publisher)
        => new(repository, publisher, Options.Create(new OutboxOption()), NullLogger<OutboxDeliveryService>.Instance);
}
