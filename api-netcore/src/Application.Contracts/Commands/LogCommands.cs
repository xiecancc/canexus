namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;

/// <summary>清空全部登录日志命令（物理删除，危险操作）。</summary>
public sealed record CleanLoginLogCommand : ICommand;

/// <summary>清空全部操作日志命令（物理删除，危险操作）。</summary>
public sealed record CleanOperLogCommand : ICommand;

/// <summary>按 Id 删除单条登录日志命令（物理删除，与清空策略一致）。</summary>
/// <param name="Id">日志 Id。</param>
public sealed record DeleteLoginLogCommand(Guid Id) : ICommand;

/// <summary>按 Id 删除单条操作日志命令（物理删除，与清空策略一致）。</summary>
/// <param name="Id">日志 Id。</param>
public sealed record DeleteOperLogCommand(Guid Id) : ICommand;
