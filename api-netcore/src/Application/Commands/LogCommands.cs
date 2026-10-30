namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Domain.Repositories;

/// <summary>清空全部登录日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（物理清空）。</param>
public sealed class CleanLoginLogCommandHandler(ILoginLogRepository loginLogRepository) : CommandHandler<CleanLoginLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(CleanLoginLogCommand command, CancellationToken cancellationToken)
    {
        await loginLogRepository.ClearAsync(cancellationToken);
    }
}

/// <summary>清空全部操作日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（物理清空）。</param>
public sealed class CleanOperLogCommandHandler(IOperLogRepository operLogRepository) : CommandHandler<CleanOperLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(CleanOperLogCommand command, CancellationToken cancellationToken)
    {
        await operLogRepository.ClearAsync(cancellationToken);
    }
}

/// <summary>删除单条登录日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（物理删除）。</param>
public sealed class DeleteLoginLogCommandHandler(ILoginLogRepository loginLogRepository) : CommandHandler<DeleteLoginLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteLoginLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await loginLogRepository.HardDeleteAsync(command.Id, cancellationToken);
    }
}

/// <summary>删除单条操作日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（物理删除）。</param>
public sealed class DeleteOperLogCommandHandler(IOperLogRepository operLogRepository) : CommandHandler<DeleteOperLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteOperLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await operLogRepository.HardDeleteAsync(command.Id, cancellationToken);
    }
}
