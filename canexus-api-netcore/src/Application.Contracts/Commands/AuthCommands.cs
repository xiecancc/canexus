
using Application.Contracts.Abstractions;
using Application.Contracts.Auth;
using Domain.Shared.Permissions;


namespace Application.Contracts.Commands;
/// <summary>账号密码登录命令（验证码校验 + 失败锁定策略）。
/// <para>标记 INonTransactionalCommand（P0-D1）：处理器主体耗时在数据库事务外\n/// （BCrypt 口令校验、Redis 验证码 / 锁定检查），包裹事务只会放大连接与行锁占用；\n/// 唯一的数据库写入（refreshToken 持久化）为单行插入，登录日志走独立连接，无事务原子性需求。</para></summary>
/// <param name="Request">登录请求。</param>
/// <param name="Ip">客户端 IP（写入登录日志）。</param>
/// <param name="UserAgent">浏览器 User-Agent（解析浏览器 / 操作系统写入登录日志）。</param>
[AllowAnonymousCommand]
public sealed record AuthLoginCommand(LoginRequest Request, string? Ip, string? UserAgent) : Request<TokenPairDto>, ICommand, INonTransactionalCommand;

/// <summary>登出命令：吊销 refreshToken（不传时吊销当前用户全部活跃令牌）。</summary>
/// <param name="RefreshToken">刷新令牌；可空。</param>
[AllowAnyAuthenticatedCommand]
public sealed record AuthLogoutCommand(string? RefreshToken) : Request, ICommand;

/// <summary>刷新令牌命令（轮换：旧 refreshToken 失效，发放新令牌对；来源信息随新令牌持久化供在线会话展示）。</summary>
/// <param name="RefreshToken">原刷新令牌。</param>
/// <param name="Ip">客户端 IP（可选，写入新 refreshToken 记录）。</param>
/// <param name="UserAgent">浏览器 User-Agent（可选，写入新 refreshToken 记录）。</param>
[AllowAnonymousCommand]
public sealed record AuthRefreshTokenCommand(string RefreshToken, string? Ip = null, string? UserAgent = null) : Request<TokenPairDto>, ICommand;

/// <summary>自助修改密码命令（校验原密码 → 更新哈希 → 清除强制改密标记 → 吊销全部刷新令牌强制重新登录）。</summary>
/// <param name="OldPassword">原密码。</param>
/// <param name="NewPassword">新密码。</param>
[AllowAnyAuthenticatedCommand]
public sealed record AuthChangePasswordCommand(string OldPassword, string NewPassword) : Request<bool>, ICommand;
