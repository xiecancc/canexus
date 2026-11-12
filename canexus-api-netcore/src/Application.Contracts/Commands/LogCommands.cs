
using Application.Contracts.Abstractions;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>按时间范围清空登录日志命令（物理删除，危险操作；须显式确认与有限时间范围）。</summary>
/// <param name="BeginTime">起始日期（含当天）。</param>
/// <param name="EndTime">结束日期（含当天；处理器归一化为次日零点作为独占上界）。</param>
/// <param name="Confirm">显式确认标志（必须为 true）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorLogininforClean)]
public sealed record LoginLogCleanCommand(DateTime BeginTime, DateTime EndTime, bool Confirm) : Request, ICommand;

/// <summary>按时间范围清空操作日志命令（物理删除，危险操作；须显式确认与有限时间范围）。</summary>
/// <param name="BeginTime">起始日期（含当天）。</param>
/// <param name="EndTime">结束日期（含当天；处理器归一化为次日零点作为独占上界）。</param>
/// <param name="Confirm">显式确认标志（必须为 true）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorOperlogClean)]
public sealed record OperLogCleanCommand(DateTime BeginTime, DateTime EndTime, bool Confirm) : Request, ICommand;

/// <summary>按 Id 删除单条登录日志命令（物理删除，与清空策略一致；Version 仅保持删除调用形态统一，物理删除不做版本校验）。</summary>
/// <param name="Id">日志 Id。</param>
/// <param name="Version">乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorLogininforRemove)]
public sealed record LoginLogDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>按 Id 删除单条操作日志命令（物理删除，与清空策略一致；Version 仅保持删除调用形态统一，物理删除不做版本校验）。</summary>
/// <param name="Id">日志 Id。</param>
/// <param name="Version">乐观锁版本号（物理删除不校验，仅为删除调用形态统一）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorOperlogRemove)]
public sealed record OperLogDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);
