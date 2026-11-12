using Application.Events;
using Domain.Entities;
using Domain.Events;
using Domain.Services.Notifications;
using Infrastructure.Shared.Caching;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Events;
/// <summary>
/// 通知渠道分发监听器单元测试：验证公告创建事件分发到全部渠道、渠道异常隔离、空渠道短路，
/// 以及 Outbox 消息重投时按「消息 Id + 渠道」幂等去重（P1-5 / D4）。
/// </summary>
[TestFixture]
public class NoticeChannelDispatcherTests
{
    /// <summary>验证分发：事件标题与正文按序投递到全部注册渠道。</summary>
    [Test]
    public async Task Handle_DispatchesToAllChannels()
    {
        var channelA = new FakeChannel("email");
        var channelB = new FakeChannel("sms");
        var dispatcher = CreateDispatcher(new MarkerCacheStub(), channelA, channelB);

        var notice = new SysNotice { Title = "标题甲", Content = "<p>正文内容</p>" };
        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(notice)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(channelA.ReceivedTitle, Is.EqualTo("标题甲"));
            Assert.That(channelA.ReceivedContent, Is.EqualTo("<p>正文内容</p>"));
            Assert.That(channelB.ReceivedTitle, Is.EqualTo("标题甲"));
        });
    }

    /// <summary>验证渠道隔离：单渠道抛出异常不影响其他渠道投递，也不向上传播。</summary>
    [Test]
    public async Task Handle_FailingChannel_DoesNotBreakOthers()
    {
        var failing = new FakeChannel("failing", throwOnSend: true);
        var healthy = new FakeChannel("email");
        var dispatcher = CreateDispatcher(new MarkerCacheStub(), failing, healthy);

        Assert.DoesNotThrowAsync(async () =>
            await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice())), CancellationToken.None));
        Assert.That(healthy.ReceivedTitle, Is.EqualTo("t"), "故障渠道不应影响后续渠道投递");
    }

    /// <summary>验证空渠道集合时静默短路（邮件未启用场景），且不写任何已投递标记。</summary>
    [Test]
    public async Task Handle_NoChannels_CompletesSilently()
    {
        var cache = new MarkerCacheStub();
        var dispatcher = CreateDispatcher(cache);

        Assert.DoesNotThrowAsync(async () =>
            await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), Guid.NewGuid()), CancellationToken.None));
        Assert.That(cache.SetCalls, Is.Zero);
    }

    /// <summary>验证非公告创建事件（其他领域事件）不触发渠道投递。</summary>
    [Test]
    public async Task Handle_NonNoticeCreatedEvent_Ignores()
    {
        var channel = new FakeChannel("email");
        var dispatcher = CreateDispatcher(new MarkerCacheStub(), channel);

        // 用户创建事件：监听器仅响应 NoticeCreatedEvent，应静默忽略
        await dispatcher.Handle(new DomainEventNotification(new UserCreatedEvent(new SysUser { UserName = "u1" })), CancellationToken.None);

        Assert.That(channel.ReceivedTitle, Is.Null, "非公告事件不应触发渠道投递");
    }

    /// <summary>
    /// 核心幂等场景：同一 Outbox 消息因其他监听器失败被重投两次，全部渠道只应收到一次通知。
    /// </summary>
    [Test]
    public async Task Handle_ReplayedOutboxMessage_SkipsDuplicateDelivery()
    {
        var channelA = new FakeChannel("email");
        var channelB = new FakeChannel("sms");
        var cache = new MarkerCacheStub();
        var dispatcher = CreateDispatcher(cache, channelA, channelB);
        var messageId = Guid.NewGuid();

        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), messageId), CancellationToken.None);
        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), messageId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(channelA.SendCount, Is.EqualTo(1), "重投同一消息不得重复发送邮件");
            Assert.That(channelB.SendCount, Is.EqualTo(1), "重投同一消息不得重复发送短信");
            Assert.That(cache.SetCalls, Is.EqualTo(2), "仅首次投递为每个渠道写入一次标记");
        });
    }

    /// <summary>不同 Outbox 消息（不同公告）互不影响，去重键必须包含消息 Id。</summary>
    [Test]
    public async Task Handle_DifferentOutboxMessageIds_DeliverSeparately()
    {
        var channel = new FakeChannel("email");
        var dispatcher = CreateDispatcher(new MarkerCacheStub(), channel);

        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), Guid.NewGuid()), CancellationToken.None);
        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), Guid.NewGuid()), CancellationToken.None);

        Assert.That(channel.SendCount, Is.EqualTo(2));
    }

    /// <summary>非 Outbox 路径（无消息 Id，如进程内直发）保持原有逐次分发语义、不写标记。</summary>
    [Test]
    public async Task Handle_NoOutboxMessageId_PreservesLegacyBehavior()
    {
        var channel = new FakeChannel("email");
        var cache = new MarkerCacheStub();
        var dispatcher = CreateDispatcher(cache, channel);

        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice())), CancellationToken.None);
        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice())), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(channel.SendCount, Is.EqualTo(2));
            Assert.That(cache.SetCalls, Is.Zero);
        });
    }

    /// <summary>
    /// 部分失败场景：故障渠道不写标记，消息重投时只补发该渠道，已成功的渠道不得被重复打扰。
    /// </summary>
    [Test]
    public async Task Handle_ChannelFailure_DoesNotWriteMarker_AndRetriesOnlyFailedChannel()
    {
        var failing = new FakeChannel("sms", throwOnSend: true);
        var healthy = new FakeChannel("email");
        var dispatcher = CreateDispatcher(new MarkerCacheStub(), failing, healthy);
        var messageId = Guid.NewGuid();

        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), messageId), CancellationToken.None);
        failing.ThrowOnSend = false;
        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), messageId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(healthy.SendCount, Is.EqualTo(1), "已成功渠道在重投时必须跳过");
            Assert.That(failing.SendCount, Is.EqualTo(2), "故障渠道应在重投时补发");
        });
    }

    /// <summary>去重键按渠道隔离：一个渠道已有标记不影响同消息的另一个渠道补发。</summary>
    [Test]
    public async Task Handle_MarkerKeyIncludesChannelName()
    {
        var email = new FakeChannel("email");
        var sms = new FakeChannel("sms");
        var cache = new MarkerCacheStub();
        var messageId = Guid.NewGuid();
        cache.Seed($"outbox:notice-channel:{messageId:N}:email", "1");
        var dispatcher = CreateDispatcher(cache, email, sms);

        await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(CreateNotice()), messageId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(email.SendCount, Is.Zero, "已标记渠道应跳过");
            Assert.That(sms.SendCount, Is.EqualTo(1), "未标记渠道仍应投递");
        });
    }

    /// <summary>构造测试用公告。</summary>
    /// <returns>最小可用的公告实体。</returns>
    private static SysNotice CreateNotice() => new() { Title = "t", Content = "c" };

    /// <summary>构造被测监听器。</summary>
    /// <param name="cache">缓存桩。</param>
    /// <param name="channels">渠道桩集合。</param>
    /// <returns>监听器实例。</returns>
    private static NoticeChannelDispatcher CreateDispatcher(MarkerCacheStub cache, params INotificationChannel[] channels)
        => new(channels, cache, NullLogger<NoticeChannelDispatcher>.Instance);

    /// <summary>测试用渠道桩：记录投递次数与最近一次内容，可切换故障状态。</summary>
    /// <param name="name">渠道名。</param>
    /// <param name="throwOnSend">投递时是否抛出异常。</param>
    private sealed class FakeChannel(string name, bool throwOnSend = false) : INotificationChannel
    {
        /// <summary>收到的最近一次投递标题（null 表示未投递）。</summary>
        public string? ReceivedTitle { get; private set; }

        /// <summary>收到的最近一次投递正文。</summary>
        public string? ReceivedContent { get; private set; }

        /// <summary>投递调用次数（含失败调用）。</summary>
        public int SendCount { get; private set; }

        /// <summary>是否在投递时抛异常（可在重投前关闭以模拟故障恢复）。</summary>
        public bool ThrowOnSend { get; set; } = throwOnSend;

        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public Task SendAsync(string title, string content, CancellationToken cancellationToken = default)
        {
            SendCount++;
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("模拟渠道故障");
            }

            ReceivedTitle = title;
            ReceivedContent = content;
            return Task.CompletedTask;
        }
    }

    /// <summary>测试用缓存桩：仅实现「已投递标记」所需的键值存取与计数。</summary>
    private sealed class MarkerCacheStub : ICache
    {
        private readonly Dictionary<string, object?> _values = [];

        /// <summary>写入次数。</summary>
        public int SetCalls { get; private set; }

        /// <summary>预置一个已存在的标记（模拟上一轮投递已成功）。</summary>
        /// <param name="key">缓存键。</param>
        /// <param name="value">标记值。</param>
        public void Seed(string key, string value) => _values[key] = value;

        /// <inheritdoc />
        public ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull
            => factory(cancellationToken);

        /// <inheritdoc />
        public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return _values.TryGetValue(key, out var value) && value is T typed
                ? ValueTask.FromResult<T?>(typed)
                : ValueTask.FromResult(default(T));
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            SetCalls++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            SetCalls++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
