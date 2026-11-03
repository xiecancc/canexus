using Application.Events;
using Domain.Entities;
using Domain.Events;
using Domain.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Events;
/// <summary>通知渠道分发监听器单元测试：验证公告创建事件分发到全部渠道、渠道异常隔离与空渠道短路。</summary>
[TestFixture]
public class NoticeChannelDispatcherTests
{
    /// <summary>验证分发：事件标题与正文按序投递到全部注册渠道。</summary>
    [Test]
    public async Task Handle_DispatchesToAllChannels()
    {
        var channelA = new FakeChannel("email");
        var channelB = new FakeChannel("sms");
        var dispatcher = new NoticeChannelDispatcher([channelA, channelB], NullLogger<NoticeChannelDispatcher>.Instance);

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
        var dispatcher = new NoticeChannelDispatcher([failing, healthy], NullLogger<NoticeChannelDispatcher>.Instance);

        Assert.DoesNotThrowAsync(async () =>
            await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(new SysNotice { Title = "t", Content = "c" })), CancellationToken.None));
        Assert.That(healthy.ReceivedTitle, Is.EqualTo("t"), "故障渠道不应影响后续渠道投递");
    }

    /// <summary>验证空渠道集合时静默短路（邮件未启用场景）。</summary>
    [Test]
    public async Task Handle_NoChannels_CompletesSilently()
    {
        var dispatcher = new NoticeChannelDispatcher([], NullLogger<NoticeChannelDispatcher>.Instance);

        Assert.DoesNotThrowAsync(async () =>
            await dispatcher.Handle(new DomainEventNotification(new NoticeCreatedEvent(new SysNotice { Title = "t", Content = "c" })), CancellationToken.None));
    }

    /// <summary>验证非公告创建事件（其他领域事件）不触发渠道投递。</summary>
    [Test]
    public async Task Handle_NonNoticeCreatedEvent_Ignores()
    {
        var channel = new FakeChannel("email");
        var dispatcher = new NoticeChannelDispatcher([channel], NullLogger<NoticeChannelDispatcher>.Instance);

        // 用户创建事件：监听器仅响应 NoticeCreatedEvent，应静默忽略
        await dispatcher.Handle(new DomainEventNotification(new UserCreatedEvent(new SysUser { UserName = "u1" })), CancellationToken.None);

        Assert.That(channel.ReceivedTitle, Is.Null, "非公告事件不应触发渠道投递");
    }

    /// <summary>测试用渠道桩。</summary>
    /// <param name="name">渠道名。</param>
    /// <param name="throwOnSend">投递时是否抛出异常。</param>
    private sealed class FakeChannel(string name, bool throwOnSend = false) : INotificationChannel
    {
        /// <summary>收到的最近一次投递标题（null 表示未投递）。</summary>
        public string? ReceivedTitle { get; private set; }

        /// <summary>收到的最近一次投递正文。</summary>
        public string? ReceivedContent { get; private set; }

        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public Task SendAsync(string title, string content, CancellationToken cancellationToken = default)
        {
            if (throwOnSend)
            {
                throw new InvalidOperationException("模拟渠道故障");
            }

            ReceivedTitle = title;
            ReceivedContent = content;
            return Task.CompletedTask;
        }
    }
}
