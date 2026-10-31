
using Application.Contracts.Abstractions;
using Application.Contracts.Auth;

namespace Application.Contracts.Commands;
/// <summary>账号密码登录命令（验证码校验 + 失败锁定策略）。</summary>
/// <param name="Request">登录请求。</param>
/// <param name="Ip">客户端 IP（写入登录日志）。</param>
/// <param name="UserAgent">浏览器 User-Agent（解析浏览器 / 操作系统写入登录日志）。</param>
public sealed record LoginCommand(LoginRequest Request, string? Ip, string? UserAgent) : ICommand<TokenPairDto>;

/// <summary>登出命令：吊销 refreshToken（不传时吊销当前用户全部活跃令牌）。</summary>
/// <param name="RefreshToken">刷新令牌；可空。</param>
public sealed record LogoutCommand(string? RefreshToken) : ICommand;

/// <summary>刷新令牌命令（轮换：旧 refreshToken 失效，发放新令牌对）。</summary>
/// <param name="RefreshToken">原刷新令牌。</param>
public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<TokenPairDto>;
