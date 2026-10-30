namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Domain.Repositories;

/// <summary>清空全部登录日志命令处理器。</summary>
public sealed class CleanLoginLogCommandHandler : CommandHandler<CleanLoginLogCommand>
{
    private readonly ILoginLogRepository _loginLogRepository;

    /// <summary>构造函数。</summary>
    /// <param name="loginLogRepository">登录日志仓储（物理清空）。</param>
    public CleanLoginLogCommandHandler(ILoginLogRepository loginLogRepository)
    {
        _loginLogRepository = loginLogRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(CleanLoginLogCommand command, CancellationToken cancellationToken)
    {
        await _loginLogRepository.ClearAsync(cancellationToken);
    }
}

/// <summary>清空全部操作日志命令处理器。</summary>
public sealed class CleanOperLogCommandHandler : CommandHandler<CleanOperLogCommand>
{
    private readonly IOperLogRepository _operLogRepository;

    /// <summary>构造函数。</summary>
    /// <param name="operLogRepository">操作日志仓储（物理清空）。</param>
    public CleanOperLogCommandHandler(IOperLogRepository operLogRepository)
    {
        _operLogRepository = operLogRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(CleanOperLogCommand command, CancellationToken cancellationToken)
    {
        await _operLogRepository.ClearAsync(cancellationToken);
    }
}

/// <summary>删除单条登录日志命令处理器。</summary>
public sealed class DeleteLoginLogCommandHandler : CommandHandler<DeleteLoginLogCommand>
{
    private readonly ILoginLogRepository _loginLogRepository;

    /// <summary>构造函数。</summary>
    /// <param name="loginLogRepository">登录日志仓储（物理删除）。</param>
    public DeleteLoginLogCommandHandler(ILoginLogRepository loginLogRepository)
    {
        _loginLogRepository = loginLogRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteLoginLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await _loginLogRepository.HardDeleteAsync(command.Id, cancellationToken);
    }
}

/// <summary>删除单条操作日志命令处理器。</summary>
public sealed class DeleteOperLogCommandHandler : CommandHandler<DeleteOperLogCommand>
{
    private readonly IOperLogRepository _operLogRepository;

    /// <summary>构造函数。</summary>
    /// <param name="operLogRepository">操作日志仓储（物理删除）。</param>
    public DeleteOperLogCommandHandler(IOperLogRepository operLogRepository)
    {
        _operLogRepository = operLogRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteOperLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await _operLogRepository.HardDeleteAsync(command.Id, cancellationToken);
    }
}
