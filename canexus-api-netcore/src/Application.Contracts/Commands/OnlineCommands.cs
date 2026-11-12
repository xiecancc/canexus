using Application.Contracts.Abstractions;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>强制下线指定在线会话命令（按会话记录 Id 吊销对应 refreshToken）。</summary>
/// <param name="TokenId">在线会话记录 Id（sys_refresh_token 主键）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorOnlineForceLogout)]
public sealed record OnlineForceLogoutCommand(Guid TokenId) : Request, ICommand;
