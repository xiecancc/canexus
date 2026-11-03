
using Application.Contracts.Abstractions;

namespace Application.Contracts.Commands;
/// <summary>清空全部登录日志命令（物理删除，危险操作）。</summary>
public sealed record LoginLogCleanCommand : Request, ICommand;

/// <summary>清空全部操作日志命令（物理删除，危险操作）。</summary>
public sealed record OperLogCleanCommand : Request, ICommand;

/// <summary>按 Id 删除单条登录日志命令（物理删除，与清空策略一致；Version 仅保持删除调用形态统一，物理删除不做版本校验）。</summary>
/// <param name="Id">日志 Id。</param>
/// <param name="Version">乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</param>
public sealed record LoginLogDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>按 Id 删除单条操作日志命令（物理删除，与清空策略一致；Version 仅保持删除调用形态统一，物理删除不做版本校验）。</summary>
/// <param name="Id">日志 Id。</param>
/// <param name="Version">乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</param>
public sealed record OperLogDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);
