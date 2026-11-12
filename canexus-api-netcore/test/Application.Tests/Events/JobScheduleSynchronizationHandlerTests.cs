using Application.Events;
using Domain.Entities;
using Domain.Events;
using Domain.Services;
using Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Events;

/// <summary>定时任务调度同步监听器单元测试：按领域事件类型验证 Quartz 调度的入 / 出同步口径。</summary>
[TestFixture]
public class JobScheduleSynchronizationHandlerTests
{
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static JobScheduleSynchronizationHandler CreateHandler(RecordingJobScheduleManager manager) =>
        new(manager, NullLogger<JobScheduleSynchronizationHandler>.Instance);

    private static SysJob CreateJob(CommonStatus status = CommonStatus.Enabled, string jobGroup = "DEFAULT") =>
        new() { Id = JobId, Status = status, JobGroup = jobGroup };

    /// <summary>验证创建事件（启用）：入调度一次，且携带创建后的任务。</summary>
    [Test]
    public async Task Handle_JobCreatedEnabled_SchedulesJob()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);
        var job = CreateJob(CommonStatus.Enabled);

        await handler.Handle(new DomainEventNotification(new JobCreatedEvent(job)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Has.Count.EqualTo(1));
            Assert.That(manager.ScheduledJobs[0].Id, Is.EqualTo(JobId));
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>验证创建事件（停用）：暂停任务只入库，不触发任何调度同步。</summary>
    [Test]
    public async Task Handle_JobCreatedDisabled_DoesNothing()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);

        await handler.Handle(new DomainEventNotification(
            new JobCreatedEvent(CreateJob(CommonStatus.Disabled))), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>验证删除事件：按删除前任务的 Id 与分组移出调度。</summary>
    [Test]
    public async Task Handle_JobDeleted_RemovesJob()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);

        await handler.Handle(new DomainEventNotification(
            new JobDeletedEvent(CreateJob(jobGroup: "GROUP_A"))), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs[0].JobId, Is.EqualTo(JobId));
            Assert.That(manager.RemovedJobs[0].JobGroup, Is.EqualTo("GROUP_A"));
        });
    }

    /// <summary>验证恢复事件（启用）：重新入调度一次。</summary>
    [Test]
    public async Task Handle_JobRestoredEnabled_SchedulesJob()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);

        await handler.Handle(new DomainEventNotification(
            new JobRestoredEvent(CreateJob(CommonStatus.Enabled))), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>验证恢复事件（停用）：不触发任何调度同步。</summary>
    [Test]
    public async Task Handle_JobRestoredDisabled_DoesNothing()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);

        await handler.Handle(new DomainEventNotification(
            new JobRestoredEvent(CreateJob(CommonStatus.Disabled))), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>验证修改事件（启用、分组未变）：仅入调度一次，不清理旧 JobKey。</summary>
    [Test]
    public async Task Handle_JobUpdatedEnabled_SchedulesJob()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);
        var before = CreateJob(CommonStatus.Disabled, "GROUP_A");
        var after = CreateJob(CommonStatus.Enabled, "GROUP_A");

        await handler.Handle(new DomainEventNotification(new JobUpdatedEvent(before, after)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>验证修改事件（停用）：按变更后分组移出调度。</summary>
    [Test]
    public async Task Handle_JobUpdatedDisabled_RemovesJob()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);
        var before = CreateJob(CommonStatus.Enabled, "GROUP_A");
        var after = CreateJob(CommonStatus.Disabled, "GROUP_A");

        await handler.Handle(new DomainEventNotification(new JobUpdatedEvent(before, after)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs[0].JobId, Is.EqualTo(JobId));
            Assert.That(manager.RemovedJobs[0].JobGroup, Is.EqualTo("GROUP_A"));
        });
    }

    /// <summary>验证修改事件（分组变更且启用）：先清理旧分组 JobKey，再按新状态入调度。</summary>
    [Test]
    public async Task Handle_JobUpdatedGroupChanged_RemovesOldKeyThenSchedules()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);
        var before = CreateJob(CommonStatus.Enabled, "OLD_GROUP");
        var after = CreateJob(CommonStatus.Enabled, "NEW_GROUP");

        await handler.Handle(new DomainEventNotification(new JobUpdatedEvent(before, after)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs, Has.Count.EqualTo(1));
            Assert.That(manager.RemovedJobs[0].JobId, Is.EqualTo(JobId));
            Assert.That(manager.RemovedJobs[0].JobGroup, Is.EqualTo("OLD_GROUP"));
        });
    }

    /// <summary>验证修改事件（分组变更且停用）：旧分组与新分组的 JobKey 均被清理。</summary>
    [Test]
    public async Task Handle_JobUpdatedGroupChangedDisabled_RemovesBothKeys()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);
        var before = CreateJob(CommonStatus.Enabled, "OLD_GROUP");
        var after = CreateJob(CommonStatus.Disabled, "NEW_GROUP");

        await handler.Handle(new DomainEventNotification(new JobUpdatedEvent(before, after)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Has.Count.EqualTo(2));
            Assert.That(manager.RemovedJobs[0].JobGroup, Is.EqualTo("OLD_GROUP"));
            Assert.That(manager.RemovedJobs[1].JobGroup, Is.EqualTo("NEW_GROUP"));
        });
    }

    /// <summary>验证未知（非定时任务）领域事件：不触发任何调度同步；立即执行相关事件同理不处理。</summary>
    [Test]
    public async Task Handle_UnknownEvent_DoesNothing()
    {
        var manager = new RecordingJobScheduleManager();
        var handler = CreateHandler(manager);

        await handler.Handle(new DomainEventNotification(
            new UserPasswordResetEvent(Guid.NewGuid())), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ScheduledJobs, Is.Empty);
            Assert.That(manager.RemovedJobs, Is.Empty);
        });
    }

    /// <summary>调度管理端口记录桩：记录入 / 出调度调用，供断言同步口径。</summary>
    private sealed class RecordingJobScheduleManager : IJobScheduleManager
    {
        /// <summary>已入调度的任务（按调用顺序）。</summary>
        public List<SysJob> ScheduledJobs { get; } = [];

        /// <summary>已移出调度的 JobKey 二元组（Id、分组，按调用顺序）。</summary>
        public List<(Guid JobId, string JobGroup)> RemovedJobs { get; } = [];

        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task ScheduleAsync(SysJob job, CancellationToken cancellationToken = default)
        {
            ScheduledJobs.Add(job);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken = default)
        {
            RemovedJobs.Add((jobId, jobGroup));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RunOnceAsync(SysJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public bool IsValidCronExpression(string cronExpression) => true;

        /// <inheritdoc />
        public bool IsValidJobType(string jobType) => true;
    }
}
