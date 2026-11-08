
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Commands;
/// <summary>账号密码登录命令处理器。</summary>
/// <remarks>构造函数。登录日志写入失败仅告警，不阻断认证（见 WriteLoginLogAsync）。</remarks>
/// <param name="userRepository">用户聚合仓储。</param>
/// <param name="loginLogRepository">登录日志仓储。</param>
/// <param name="captchaService">验证码服务。</param>
/// <param name="tokenService">令牌服务。</param>
/// <param name="tokenSubjectFactory">令牌主体工厂。</param>
/// <param name="passwordHasher">口令哈希校验。</param>
/// <param name="cache">失败计数缓存。</param>
/// <param name="distributedLock">失败计数分布式锁。</param>
/// <param name="tenantAccessValidator">租户准入校验。</param>
/// <param name="tenantContext">当前租户上下文。</param>
/// <param name="loginLockOptions">登录失败锁定阈值（Security:LoginLock，EX-05 阈值 Option 化）。</param>
/// <param name="logger">日志器（登录日志写入失败仅告警）。</param>
public sealed class AuthLoginCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    ILoginLogRepository loginLogRepository,
    ICaptchaService captchaService,
    ITokenService tokenService,
    ITokenSubjectFactory tokenSubjectFactory,
    IPasswordHasher passwordHasher,
    ICache cache,
    IDistributedLock distributedLock,
    ITenantAccessValidator tenantAccessValidator,
    ITenantContext tenantContext,
    IOptionsMonitor<LoginLockOption> loginLockOptions,
    ILogger<AuthLoginCommandHandler> logger) : RequestHandler<AuthLoginCommand, TokenPairDto>
{
    /// <summary>登录失败计数缓存 key 前缀（key 格式：{prefix}{userName}）。</summary>
    public const string LoginFailCacheKeyPrefix = "auth:login-fail:";

    /// <summary>允许的最大连续失败次数（达到即锁定）：来自 Security:LoginLock:MaxFailedAttempts。</summary>
    private int MaxLoginFailCount => loginLockOptions.CurrentValue.MaxFailedAttempts;

    /// <summary>锁定时长（失败窗口，每次失败滑动重置）：来自 Security:LoginLock:LockMinutes。</summary>
    private TimeSpan LockDuration => TimeSpan.FromMinutes(loginLockOptions.CurrentValue.LockMinutes);

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

        // 3.1 租户归属校验：请求头指定的租户须与账号归属一致；不依赖 QueryFilter 是否作用于当前 ORM 查询，
        // 统一按「账号或密码错误」拒绝，避免泄露其他租户下同名账号的存在性。
        // 未携带租户头（平台范围 / 多租户关闭）时保持既有登录语义。
        if (tenantContext.TenantId is { } requestedTenant && user.TenantId != requestedTenant)
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
        if (state is not null && state.Count >= MaxLoginFailCount)
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

        // 显式 TTL：L1 1 分钟（本实例快速判定）/ L2 取配置锁定窗口（跨实例一致）
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
    private async Task WriteLoginLogAsync(string userName, string? ip, string? userAgent, bool success, string msg, CancellationToken cancellationToken, bool independent = false)
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

        try
        {
            // 登录日志属审计旁路：日志库不可用 / 写入失败不得顶替认证结果（尤其失败路径应抛业务异常而非日志异常）
            if (independent)
            {
                await loginLogRepository.InsertIndependentAsync(log, cancellationToken);
            }
            else
            {
                await loginLogRepository.InsertAsync(log, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "写入登录日志失败：账号 {UserName}，结果 {Success}", userName, success);
        }
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

/// <summary>刷新令牌命令处理器（含令牌家族重用检测）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="userRepository">用户聚合仓储。</param>
/// <param name="refreshTokenRepository">刷新令牌仓储（家族吊销）。</param>
/// <param name="tokenService">令牌服务。</param>
/// <param name="tokenVersionService">令牌版本服务（吊销会话 accessToken）。</param>
/// <param name="tokenSubjectFactory">令牌主体工厂。</param>
/// <param name="tenantAccessValidator">租户准入校验。</param>
/// <param name="multiTenancyOption">多租户配置。</param>
/// <param name="logger">日志器（安全事件留痕）。</param>
public sealed class AuthRefreshTokenCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    ISysRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    ITokenVersionService tokenVersionService,
    ITokenSubjectFactory tokenSubjectFactory,
    ITenantAccessValidator tenantAccessValidator,
    MultiTenancyOption multiTenancyOption,
    ILogger<AuthRefreshTokenCommandHandler> logger) : RequestHandler<AuthRefreshTokenCommand, TokenPairDto>
{
    /// <summary>并发轮换宽限窗口：吊销后该时长内的旧令牌重用视为正常客户端并发，仅拒绝不吊销家族。</summary>
    private static readonly TimeSpan ReuseGraceWindow = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    protected override async Task<TokenPairDto> HandleAsync(AuthRefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var active = await tokenService.GetActiveRefreshTokenAsync(command.RefreshToken, cancellationToken);
        if (active is null)
        {
            await HandleRevokedTokenReuseAsync(command.RefreshToken, cancellationToken);
            throw new BusinessException(ErrorCodes.AuthRefreshTokenInvalid, "刷新令牌无效或已过期，请重新登录");
        }

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

    /// <summary>
    /// 令牌家族重用检测：旧 refreshToken 在轮换/吊销后被再次使用，说明该家族可能已泄露，
    /// 吊销同一会话下的全部 refreshToken 与 accessToken。
    /// <para>吊销一律走独立连接（仓储 CopyNew）与缓存，不依赖后续会回滚的写命令事务；
    /// 吊销后 10 秒内的重用视为正常客户端并发，只拒绝不吊销，避免误伤。</para>
    /// </summary>
    /// <param name="refreshToken">被重用的刷新令牌串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task HandleRevokedTokenReuseAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var revoked = await tokenService.FindRevokedRefreshTokenAsync(refreshToken, cancellationToken);
        if (revoked is null || revoked.SessionId == Guid.Empty)
        {
            return;
        }

        if (DateTime.UtcNow - revoked.RevokedAt <= ReuseGraceWindow)
        {
            // 并发轮换（如移动端与 Web 同时刷新）：仅拒绝本次请求，不吊销整个家族
            return;
        }

        logger.LogWarning(
            "检测到刷新令牌家族重用：用户 {UserId} 会话 {SessionId}，令牌于 {RevokedAt:O} 吊销后被再次使用，已吊销该会话全部令牌",
            revoked.UserId, revoked.SessionId, revoked.RevokedAt);

        // 独立连接写入：本命令随后抛业务异常，外层事务会回滚，安全吊销不能随之回滚
        await refreshTokenRepository.RevokeAllForSessionAsync(revoked.SessionId, DateTime.UtcNow, cancellationToken);
        // 缓存吊销标记：该会话已签发的 accessToken 下一次请求即 401
        await tokenVersionService.RevokeSessionAsync(revoked.SessionId, cancellationToken);
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
