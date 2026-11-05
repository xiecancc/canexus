
using Application.Abstractions;
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Services.Events;
using Domain.Services.Security;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;

namespace Application.Commands;
/// <summary>账号密码登录命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthLoginCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    ILoginLogRepository loginLogRepository,
    ICaptchaService captchaService,
    ITokenService tokenService,
    ITokenSubjectFactory tokenSubjectFactory,
    IPasswordHasher passwordHasher,
    ICache cache,
    IDistributedLock distributedLock,
    ITenantAccessValidator tenantAccessValidator) : RequestHandler<AuthLoginCommand, TokenPairDto>
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
            await WriteLoginLogAsync(user.UserName, command.Ip, command.UserAgent, success: false, "账号已停用", cancellationToken, independent: true);
            throw new BusinessException(ErrorCodes.UserDisabled, "账号已停用，请联系管理员");
        }

        // 3.5 租户校验（P1-A1）：租户归属用户的租户须存在、启用且未到期；停用 / 到期 / 已删除租户的用户不可登录，
        // 失败原因经独立连接写入登录日志（随后抛出的业务异常会回滚命令事务，审计日志必须存活于回滚之外）
        if (user.TenantId is { } tenantId)
        {
            var tenantStatus = await tenantAccessValidator.ValidateAsync(tenantId, cancellationToken);
            if (tenantStatus != TenantAccessStatus.Ok)
            {
                var (tenantCode, tenantMsg) = tenantStatus switch
                {
                    TenantAccessStatus.NotFound => (ErrorCodes.TenantNotFound, "租户不存在或已被删除，请联系管理员"),
                    TenantAccessStatus.Disabled => (ErrorCodes.TenantDisabled, "租户已停用，请联系管理员"),
                    _ => (ErrorCodes.TenantExpired, "租户已到期，请联系管理员")
                };
                await WriteLoginLogAsync(user.UserName, command.Ip, command.UserAgent, success: false, tenantMsg, cancellationToken, independent: true);
                throw new BusinessException(tenantCode, tenantMsg);
            }
        }

        // 4. 登录成功：清除失败计数；日志与令牌发放的写一致性由事务管道（TransactionBehavior）保证
        await cache.RemoveAsync(LoginFailCacheKeyPrefix + request.UserName, cancellationToken);
        var subject = await tokenSubjectFactory.BuildAsync(user, cancellationToken);

        await WriteLoginLogAsync(user.UserName, command.Ip, command.UserAgent, success: true, "登录成功", cancellationToken);
        var pair = await tokenService.GenerateAsync(subject, command.Ip, command.UserAgent, cancellationToken);
        return new TokenPairDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn = pair.ExpiresIn,
            MustChangePassword = user.MustChangePassword == true
        };
    }

    /// <summary>检查账号锁定状态：连续失败达到上限则抛出锁定异常（含剩余分钟数）。</summary>
    private async Task CheckLockStateAsync(string userName, CancellationToken cancellationToken)
    {
        var state = await cache.GetAsync<LoginFailState>(LoginFailCacheKeyPrefix + userName, cancellationToken);
        if (state is { Count: >= MaxLoginFailCount })
        {
            var remaining = LockDuration - (DateTime.UtcNow - state.LastFailTime);
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            throw new BusinessException(ErrorCodes.AuthAccountLocked, $"密码连续错误次数过多，账号已锁定，请约 {minutes} 分钟后重试");
        }
    }

    /// <summary>记录一次登录失败：失败计数 +1（滑动窗口重置）并写入登录日志。</summary>
    /// <remarks>
    /// 计数读-改-写经分布式锁保护（按用户名分区）：多实例并发失败时避免计数丢失导致锁定阈值被稀释；
    /// 未获取到锁（等待超时 / Redis 故障）降级为无锁路径，认证端点限流（auth 策略）兜底暴力尝试。
    /// 失败日志经独立连接写入：随后抛出的业务异常会回滚命令事务，审计日志必须存活于回滚之外。
    /// </remarks>
    private async Task RecordLoginFailAsync(string userName, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var key = LoginFailCacheKeyPrefix + userName;
        await using var handle = await distributedLock.AcquireAsync(
            key, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500), cancellationToken);

        var current = await cache.GetAsync<LoginFailState>(key, cancellationToken);
        var next = current is not null
            ? new LoginFailState(current.Count + 1, DateTime.UtcNow)
            : new LoginFailState(1, DateTime.UtcNow);

        // 显式 TTL：L1 1 分钟（本实例快速判定）/ L2 10 分钟（锁定窗口，跨实例一致）
        await cache.SetAsync(key, next, new CacheTtlProfile(TimeSpan.FromMinutes(1), LockDuration), cancellationToken);

        await WriteLoginLogAsync(userName, ip, userAgent, success: false, "账号或密码错误", cancellationToken, independent: true);
    }

    /// <summary>写入登录日志（User-Agent 原样存储，浏览器 / 操作系统解析留给展示层）。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="ip">来源 IP。</param>
    /// <param name="userAgent">User-Agent。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="msg">结果描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="independent">是否经独立连接写入（失败审计路径须为 true，存活于事务回滚之外）。</param>
    private Task WriteLoginLogAsync(string userName, string? ip, string? userAgent, bool success, string msg, CancellationToken cancellationToken, bool independent = false)
    {
        var log = new SysLoginLog
        {
            UserName = userName,
            Ip = ip,
            UserAgent = userAgent,
            Status = success
                ? ExecutionStatus.Success
                : ExecutionStatus.Failure,
            Msg = msg,
            LoginTime = DateTime.UtcNow
        };
        return independent
            ? loginLogRepository.InsertIndependentAsync(log, cancellationToken)
            : loginLogRepository.InsertAsync(log, cancellationToken);
    }
}

/// <summary>登出命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthLogoutCommandHandler(
    ISysRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    ITokenVersionService tokenVersionService,
    IUserContext currentUser) : RequestHandler<AuthLogoutCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(AuthLogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            // 单设备登出：吊销该刷新令牌所属会话（accessToken 携带同一 sid，下一次请求即 401），
            // 同用户其他设备使用不同 sid，不受影响
            var active = await tokenService.GetActiveRefreshTokenAsync(command.RefreshToken, cancellationToken);
            var sessionId = active?.SessionId is { } activeSessionId && activeSessionId != Guid.Empty
                ? activeSessionId
                : RefreshTokenFormat.TryParseSessionId(command.RefreshToken);
            if (sessionId is { } sid && sid != Guid.Empty)
            {
                await tokenVersionService.RevokeSessionAsync(sid, cancellationToken);
            }

            await tokenService.RevokeAsync(command.RefreshToken, cancellationToken);
            return;
        }

        // 未指定令牌：吊销当前用户全部活跃 refreshToken，并递增令牌版本（强制下线全部会话）
        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        // 单条 UPDATE 批量吊销全部活跃令牌
        await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);
        await tokenVersionService.RevokeAllAsync(userId, cancellationToken);
    }
}

/// <summary>刷新令牌命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthRefreshTokenCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    ITokenService tokenService,
    ITokenSubjectFactory tokenSubjectFactory,
    ITenantAccessValidator tenantAccessValidator,
    MultiTenancyOption multiTenancyOption) : RequestHandler<AuthRefreshTokenCommand, TokenPairDto>
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

        // 租户状态全链路校验（S1）：刷新是 accessToken 的续签入口，必须在签发新令牌前复查归属租户可用性；
        // 多租户关闭时保持原行为（不做租户校验），避免误伤平台用户。
        if (multiTenancyOption.Enabled && user.TenantId is { } userTenantId)
        {
            var tenantStatus = await tenantAccessValidator.ValidateAsync(userTenantId, cancellationToken);
            if (tenantStatus != TenantAccessStatus.Ok)
            {
                var message = tenantStatus switch
                {
                    TenantAccessStatus.NotFound => "归属租户不存在或已被删除，无法刷新令牌，请重新登录",
                    TenantAccessStatus.Disabled => "租户已停用，无法刷新令牌，请联系管理员",
                    _ => "租户已到期，无法刷新令牌，请联系管理员"
                };
                throw new BusinessException(ErrorCodes.TenantForbidden, message);
            }
        }

        // 轮换：吊销旧令牌 + 发放新对（同一事务保证一致性，事务由管道自动开启）
        var subject = await tokenSubjectFactory.BuildAsync(user, cancellationToken);
        await tokenService.RevokeAsync(command.RefreshToken, cancellationToken);
        // 轮换沿用同一会话标识：刷新后旧 accessToken 仍可在自然过期前使用，但登出 / 强制下线可按会话统一吊销
        var pair = await tokenService.GenerateAsync(subject, command.Ip, command.UserAgent, cancellationToken, active.SessionId);
        return new TokenPairDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn = pair.ExpiresIn,
            MustChangePassword = user.MustChangePassword == true
        };
    }
}

/// <summary>自助修改密码命令处理器：校验原密码 → 更新密码哈希并清除强制改密标记 → 吊销全部刷新令牌（强制重新登录）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthChangePasswordCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    ISysRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IUserContext currentUser,
    IDomainEventPublisher domainEventPublisher) : RequestHandler<AuthChangePasswordCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(AuthChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        var user = await userRepository.GetAsync([it => it.Id == userId], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, "用户不存在");

        if (!passwordHasher.Verify(command.OldPassword, user.PasswordHash))
        {
            throw new BusinessException(ErrorCodes.UserPasswordError, "原密码错误");
        }

        // 更新密码哈希并清除强制改密标记（乐观锁版本沿用读取到的当前版本，更新时由仓储递增）
        user.PasswordHash = passwordHasher.Hash(command.NewPassword);
        user.MustChangePassword = false;
        await userRepository.UpdateAsync(user, cancellationToken);

        // 密码变更后吊销全部刷新令牌：旧会话（除当前 accessToken 外）强制下线，需以新密码重新登录
        await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);

        // 关键节点发布密码重置领域事件（权限缓存失效等监听器按用户 Id 响应，不携带敏感状态）
        await domainEventPublisher.PublishAsync(new UserPasswordResetEvent(userId), cancellationToken);
        return true;
    }
}
