
using Application.Abstractions;
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Services.Security;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;

namespace Application.Commands;
/// <summary>账号密码登录命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthLoginCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    ILoginLogRepository loginLogRepository,
    ICaptchaService captchaService,
    ITokenService tokenService,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICache cache) : RequestHandler<AuthLoginCommand, TokenPairDto>
{
    /// <summary>登录失败计数缓存 key 前缀（key 格式：{prefix}{userName}）。</summary>
    public const string LoginFailCacheKeyPrefix = "auth:login-fail:";

    /// <summary>允许的最大连续失败次数（达到即锁定）。</summary>
    private const int MaxLoginFailCount = 5;

    /// <summary>锁定时长（失败窗口，每次失败滑动重置）。</summary>
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    /// <summary>登录失败计数状态。</summary>
    /// <param name="Count">连续失败次数。</param>
    /// <param name="LastFailTime">最近一次失败时间（用于计算锁定剩余时间）。</param>
    private sealed record LoginFailState(int Count, DateTime LastFailTime);

    /// <inheritdoc />
    protected override async Task<TokenPairDto> HandleAsync(AuthLoginCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. 校验验证码（一次性，校验后即失效）
        if (!await captchaService.ValidateAsync(request.CaptchaId, request.CaptchaCode, cancellationToken))
        {
            throw new BusinessException(ErrorCodes.AuthCaptchaError, "验证码错误或已过期");
        }

        // 2. 锁定检查：同一用户名连续失败达到上限后锁定一段时间
        await CheckLockStateAsync(request.UserName, cancellationToken);

        // 3. 校验账号与密码
        var user = (await userRepository.GetListAsync([u => u.UserName == request.UserName], cancellationToken: cancellationToken))
            .FirstOrDefault();
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordLoginFailAsync(request.UserName, command.Ip, command.UserAgent, cancellationToken);
            throw new BusinessException(ErrorCodes.AuthAccountOrPasswordError, "账号或密码错误");
        }

        if (user.Status != CommonStatus.Enabled)
        {
            await WriteLoginLogAsync(user.UserName, command.Ip, command.UserAgent, success: false, "账号已停用", cancellationToken);
            throw new BusinessException(ErrorCodes.UserDisabled, "账号已停用，请联系管理员");
        }

        // 4. 登录成功：清除失败计数，日志与令牌发放放在同一事务保证写一致
        await cache.RemoveAsync(LoginFailCacheKeyPrefix + request.UserName, cancellationToken);
        var subject = await BuildSubjectAsync(user, cancellationToken);

        return await unitOfWork.RunInTransactionAsync(async () =>
        {
            await WriteLoginLogAsync(user.UserName, command.Ip, command.UserAgent, success: true, "登录成功", cancellationToken);
            var pair = await tokenService.GenerateAsync(subject, cancellationToken);
            return new TokenPairDto { AccessToken = pair.AccessToken, RefreshToken = pair.RefreshToken, ExpiresIn = pair.ExpiresIn };
        }, cancellationToken);
    }

    /// <summary>检查账号锁定状态：连续失败达到上限则抛出锁定异常（含剩余分钟数）。</summary>
    private async Task CheckLockStateAsync(string userName, CancellationToken cancellationToken)
    {
        var state = await cache.GetAsync<LoginFailState>(LoginFailCacheKeyPrefix + userName, cancellationToken);
        if (state is { Count: >= MaxLoginFailCount })
        {
            var remaining = LockDuration - (DateTime.Now - state.LastFailTime);
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            throw new BusinessException(ErrorCodes.AuthAccountLocked, $"密码连续错误次数过多，账号已锁定，请约 {minutes} 分钟后重试");
        }
    }

    /// <summary>记录一次登录失败：失败计数 +1（滑动窗口重置）并写入登录日志。</summary>
    private async Task RecordLoginFailAsync(string userName, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var key = LoginFailCacheKeyPrefix + userName;
        var current = await cache.GetAsync<LoginFailState>(key, cancellationToken);
        var next = current is not null
            ? new LoginFailState(current.Count + 1, DateTime.Now)
            : new LoginFailState(1, DateTime.Now);

        // 显式 TTL：L1 1 分钟（本实例快速判定）/ L2 10 分钟（锁定窗口，跨实例一致）
        await cache.SetAsync(key, next, new CacheTtlProfile(TimeSpan.FromMinutes(1), LockDuration), cancellationToken);

        await WriteLoginLogAsync(userName, ip, userAgent, success: false, "账号或密码错误", cancellationToken);
    }

    /// <summary>写入登录日志（User-Agent 原样存储，浏览器 / 操作系统解析留给展示层）。</summary>
    private Task WriteLoginLogAsync(string userName, string? ip, string? userAgent, bool success, string msg, CancellationToken cancellationToken)
    {
        return loginLogRepository.InsertAsync(new SysLoginLog
        {
            UserName = userName,
            Ip = ip,
            UserAgent = userAgent,
            Status = success
                ? ExecutionStatus.Success
                : ExecutionStatus.Failure,
            Msg = msg,
            LoginTime = DateTime.Now
        }, cancellationToken);
    }

    /// <summary>构造令牌主题：加载用户启用角色（Id 与 roleKey）。</summary>
    private async Task<TokenSubject> BuildSubjectAsync(SysUser user, CancellationToken cancellationToken)
    {
        var roles = await GetUserRolesAsync(user.Id, cancellationToken);
        return new TokenSubject(
            user.Id,
            user.UserName,
            user.DeptId,
            roles.Select(r => r.Id).ToList(),
            roles.Select(r => r.RoleKey).ToList(),
            user.TenantId);
    }

    /// <summary>查询用户启用状态的角色列表。</summary>
    private async Task<List<SysRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == userId], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)], cancellationToken: cancellationToken);
    }
}

/// <summary>登出命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthLogoutCommandHandler(
    ISysRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUserContext currentUser) : RequestHandler<AuthLogoutCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(AuthLogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            await tokenService.RevokeAsync(command.RefreshToken, cancellationToken);
            return;
        }

        // 未指定令牌：吊销当前用户全部活跃 refreshToken（强制下线全部会话）
        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        // 单条 UPDATE 批量吊销全部活跃令牌
        await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.Now, cancellationToken);
    }
}

/// <summary>刷新令牌命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthRefreshTokenCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : RequestHandler<AuthRefreshTokenCommand, TokenPairDto>
{
    /// <inheritdoc />
    protected override async Task<TokenPairDto> HandleAsync(AuthRefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var active = await tokenService.GetActiveRefreshTokenAsync(command.RefreshToken, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.AuthRefreshTokenInvalid, "刷新令牌无效或已过期，请重新登录");

        var user = await userRepository.GetAsync([it => it.Id == active.UserId], cancellationToken);
        if (user is null || user.Status != CommonStatus.Enabled)
        {
            throw new BusinessException(ErrorCodes.AuthRefreshTokenInvalid, "刷新令牌无效或已过期，请重新登录");
        }

        // 轮换：吊销旧令牌 + 发放新对（同一事务保证一致性）
        var subject = await BuildSubjectAsync(user, cancellationToken);
        return await unitOfWork.RunInTransactionAsync(async () =>
        {
            await tokenService.RevokeAsync(command.RefreshToken, cancellationToken);
            var pair = await tokenService.GenerateAsync(subject, cancellationToken);
            return new TokenPairDto { AccessToken = pair.AccessToken, RefreshToken = pair.RefreshToken, ExpiresIn = pair.ExpiresIn };
        }, cancellationToken);
    }

    /// <summary>构造令牌主题：加载用户启用角色（Id 与 roleKey）。</summary>
    private async Task<TokenSubject> BuildSubjectAsync(SysUser user, CancellationToken cancellationToken)
    {
        var roles = await GetUserRolesAsync(user.Id, cancellationToken);
        return new TokenSubject(
            user.Id,
            user.UserName,
            user.DeptId,
            roles.Select(r => r.Id).ToList(),
            roles.Select(r => r.RoleKey).ToList(),
            user.TenantId);
    }

    /// <summary>查询用户启用状态的角色列表。</summary>
    private async Task<List<SysRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == userId], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)], cancellationToken: cancellationToken);
    }
}
