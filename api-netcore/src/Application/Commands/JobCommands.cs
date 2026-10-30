namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Jobs;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services;
using Domain.Services.Rules;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建定时任务命令处理器。</summary>
public sealed class CreateJobCommandHandler : CommandHandler<CreateJobCommand, Guid>
{
    private readonly IAggregateRepository<SysJob> _jobRepository;
    private readonly IJobScheduleManager _scheduleManager;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储。</param>
    /// <param name="scheduleManager">任务调度管理端口（Cron / 任务类型校验与调度同步）。</param>
    public CreateJobCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IMapper mapper)
    {
        _jobRepository = jobRepository;
        _scheduleManager = scheduleManager;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateJobCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = _mapper.Map<SysJob>(request);
        var inserted = await _jobRepository.InsertAsync(entity, cancellationToken);

        // 运行中任务创建后立即入调度；暂停任务仅入库
        if (request.Status == CommonStatus.Enabled)
        {
            await _scheduleManager.ScheduleAsync(inserted, cancellationToken);
        }

        return inserted.Id;
    }
}

/// <summary>删除定时任务命令处理器。</summary>
public sealed class DeleteJobCommandHandler : CommandHandler<DeleteJobCommand>
{
    private readonly IAggregateRepository<SysJob> _jobRepository;
    private readonly IJobScheduleManager _scheduleManager;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储。</param>
    /// <param name="scheduleManager">任务调度管理端口（删除后移出调度）。</param>
    public DeleteJobCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager)
    {
        _jobRepository = jobRepository;
        _scheduleManager = scheduleManager;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteJobCommand command, CancellationToken cancellationToken)
    {
        var entity = await _jobRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        await _scheduleManager.RemoveAsync(entity.Id, entity.JobGroup, cancellationToken);
        await _jobRepository.DeleteAsync(entity, cancellationToken);
    }
}

/// <summary>立即触发一次执行命令处理器。</summary>
public sealed class RunJobOnceCommandHandler : CommandHandler<RunJobOnceCommand>
{
    private readonly IAggregateRepository<SysJob> _jobRepository;
    private readonly IJobScheduleManager _scheduleManager;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储。</param>
    /// <param name="scheduleManager">任务调度管理端口（触发执行与任务类型校验）。</param>
    public RunJobOnceCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager)
    {
        _jobRepository = jobRepository;
        _scheduleManager = scheduleManager;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(RunJobOnceCommand command, CancellationToken cancellationToken)
    {
        var entity = await _jobRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        await _scheduleManager.RunOnceAsync(entity, cancellationToken);
    }
}

/// <summary>更新定时任务命令处理器。</summary>
public sealed class UpdateJobCommandHandler : CommandHandler<UpdateJobCommand>
{
    private readonly IAggregateRepository<SysJob> _jobRepository;
    private readonly IJobScheduleManager _scheduleManager;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储。</param>
    /// <param name="scheduleManager">任务调度管理端口（Cron / 任务类型校验与调度同步）。</param>
    public UpdateJobCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IMapper mapper)
    {
        _jobRepository = jobRepository;
        _scheduleManager = scheduleManager;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateJobCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _jobRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        var jobName = request.JobName.Trim();
        var jobGroup = request.JobGroup.Trim();

        var oldGroup = entity.JobGroup;
        _mapper.Map(request, entity);
        await _jobRepository.UpdateAsync(entity, cancellationToken);

        // 分组变更时旧 JobKey 失效，需一并清理旧调度
        if (!string.Equals(oldGroup, jobGroup, StringComparison.Ordinal))
        {
            await _scheduleManager.RemoveAsync(command.Id, oldGroup, cancellationToken);
        }

        if (request.Status == CommonStatus.Enabled)
        {
            await _scheduleManager.ScheduleAsync(entity, cancellationToken);
        }
        else
        {
            await _scheduleManager.RemoveAsync(command.Id, jobGroup, cancellationToken);
        }
    }
}

/// <summary>修改定时任务状态命令处理器。</summary>
public sealed class UpdateJobStatusCommandHandler : CommandHandler<UpdateJobStatusCommand>
{
    private readonly IAggregateRepository<SysJob> _jobRepository;
    private readonly IJobScheduleManager _scheduleManager;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储。</param>
    /// <param name="scheduleManager">任务调度管理端口（启停同步调度）。</param>
    public UpdateJobStatusCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager)
    {
        _jobRepository = jobRepository;
        _scheduleManager = scheduleManager;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateJobStatusCommand command, CancellationToken cancellationToken)
    {
        var entity = await _jobRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        entity.Status = command.Request.Status;
        await _jobRepository.UpdateAsync(entity, cancellationToken);

        if (command.Request.Status == CommonStatus.Enabled)
        {
            await _scheduleManager.ScheduleAsync(entity, cancellationToken);
        }
        else
        {
            await _scheduleManager.RemoveAsync(entity.Id, entity.JobGroup, cancellationToken);
        }
    }
}
