
using Application.Contracts.Commands;
using Application.Contracts.Jobs;
using Application.Validators;
using Domain.Entities;
using Domain.Services;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation.TestHelper;
using NUnit.Framework;
using Quartz;

namespace Application.Tests.Validators;
/// <summary>定时任务写入请求验证器单元测试：Cron 与任务类型经调度端口判定（非法即抛业务异常，错误码 CronInvalid / JobTypeInvalid）。</summary>
[TestFixture]
public class JobValidatorTests
{
    /// <summary>构造合法的任务写入请求（Cron 与任务类型由各用例覆写）。</summary>
    private static JobInputRequest BuildRequest() => new()
    {
        JobName = "示例任务",
        JobGroup = "DEFAULT",
        CronExpression = "0 0/5 * * * ?",
        JobType = typeof(TestJob).FullName!
    };

    /// <summary>验证合法请求（正确 Cron + 可解析的任务类型）不产生任何校验失败。</summary>
    [Test]
    public void Create_ValidRequest_NoFailures()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: true);
        var result = new JobCreateCommandValidator(manager)
            .TestValidate(new JobCreateCommand(BuildRequest()));
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>验证任务名称为空时校验产生失败。</summary>
    [Test]
    public void Create_EmptyJobName_HasFailure()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: true);
        var request = BuildRequest();
        request.JobName = "   ";
        var result = new JobCreateCommandValidator(manager)
            .TestValidate(new JobCreateCommand(request));
        result.ShouldHaveValidationErrorFor(command => command.Request.JobName);
    }

    /// <summary>验证任务名称超过 50 字符时校验产生失败。</summary>
    [Test]
    public void Create_JobNameTooLong_HasFailure()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: true);
        var request = BuildRequest();
        request.JobName = new string('长', 51);
        var result = new JobCreateCommandValidator(manager)
            .TestValidate(new JobCreateCommand(request));
        result.ShouldHaveValidationErrorFor(command => command.Request.JobName);
    }

    /// <summary>验证 Cron 表达式非法（调度端口判定不可用）时抛出 CronInvalid 业务异常。</summary>
    [Test]
    public void Create_InvalidCron_ThrowsCronInvalid()
    {
        var manager = new FakeScheduleManager(validCron: false, validJobType: true);
        var exception = Assert.Throws<BusinessException>(() =>
            new JobCreateCommandValidator(manager).TestValidate(new JobCreateCommand(BuildRequest())));
        Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.CronInvalid));
    }

    /// <summary>验证任务类型非法（调度端口判定不可解析）时抛出 JobTypeInvalid 业务异常。</summary>
    [Test]
    public void Create_InvalidJobType_ThrowsJobTypeInvalid()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: false);
        var exception = Assert.Throws<BusinessException>(() =>
            new JobCreateCommandValidator(manager).TestValidate(new JobCreateCommand(BuildRequest())));
        Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.JobTypeInvalid));
    }

    /// <summary>验证 Cron 与任务类型同时非法时 Cron 校验先行（短路抛出 CronInvalid）。</summary>
    [Test]
    public void Create_InvalidCronAndJobType_CronCheckedFirst()
    {
        var manager = new FakeScheduleManager(validCron: false, validJobType: false);
        var exception = Assert.Throws<BusinessException>(() =>
            new JobCreateCommandValidator(manager).TestValidate(new JobCreateCommand(BuildRequest())));
        Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.CronInvalid));
    }

    /// <summary>验证更新命令合法请求不产生任何校验失败（更新与创建共用请求验证器）。</summary>
    [Test]
    public void Update_ValidRequest_NoFailures()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: true);
        var result = new JobUpdateCommandValidator(manager)
            .TestValidate(new JobUpdateCommand(Guid.NewGuid(), Guid.NewGuid(), BuildRequest()));
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>验证更新命令版本号为空时校验产生失败（乐观锁入口兜底）。</summary>
    [Test]
    public void Update_EmptyVersion_HasFailure()
    {
        var manager = new FakeScheduleManager(validCron: true, validJobType: true);
        var result = new JobUpdateCommandValidator(manager)
            .TestValidate(new JobUpdateCommand(Guid.NewGuid(), Guid.Empty, BuildRequest()));
        result.ShouldHaveValidationErrorFor(command => command.Version);
    }

    /// <summary>调度管理端口桩：按构造参数返回 Cron / 任务类型的合法性判定结果。</summary>
    /// <param name="validCron">Cron 合法性。</param>
    /// <param name="validJobType">任务类型合法性。</param>
    private sealed class FakeScheduleManager(bool validCron, bool validJobType) : IJobScheduleManager
    {
        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task ScheduleAsync(SysJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task RemoveAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task RunOnceAsync(SysJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public bool IsValidCronExpression(string cronExpression) => validCron;

        /// <inheritdoc />
        public bool IsValidJobType(string jobType) => validJobType;
    }
}

/// <summary>测试用 Quartz 任务实现（供验证器以短格式类型名解析为合法任务类型）。</summary>
public sealed class TestJob : IJob
{
    /// <summary>空实现（仅用于类型解析合法性验证）。</summary>
    /// <param name="context">执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
