
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Auth;
using Infrastructure.Security;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Auth;
/// <summary>
/// 基于 JWT 的令牌服务：发放 accessToken（HS256 签名）与 refreshToken（随机 64 位十六进制串，仅以 SHA-256 哈希持久化到 sys_refresh_token 表）。
/// <para>claims 约定：sub=用户 Id、unique_name=用户名、deptId=部门、roleId/role=角色、tenantId=租户。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="options">JWT 配置监视器。</param>
/// <param name="refreshTokenRepository">刷新令牌仓储。</param>
/// <param name="logger">日志器。</param>
public sealed class JwtTokenService(IOptionsMonitor<JwtOption> options, ISysRefreshTokenRepository refreshTokenRepository, ILogger<JwtTokenService> logger) : ITokenService
{
    /// <summary>令牌处理器：关闭入站 claim 映射，保持 sub / unique_name / role 等原始名称（与 JwtBearer 配置一致）。</summary>
    private static readonly JwtSecurityTokenHandler TokenHandler = new() { MapInboundClaims = false };

    /// <summary>当前 JWT 配置（经 IOptionsMonitor 读取，Secret 等变更支持热加载）。</summary>
    private JwtOption Options => options.CurrentValue;

    /// <summary>
    /// 构造令牌校验参数（JWT Bearer 与 accessToken 校验共用，保证两边行为一致）。
    /// </summary>
    /// <param name="options">JWT 配置。</param>
    /// <param name="secretProvider">
    /// 动态密钥提供者（可选）：提供时 IssuerSigningKeyResolver 在每次验签时读取最新 Secret，
    /// 支持运行期更换签名密钥（热加载）；不提供则使用 options 内的静态密钥。
    /// </param>
    /// <returns>校验参数。</returns>
    public static TokenValidationParameters BuildValidationParameters(JwtOption options, Func<string>? secretProvider = null) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        // 固定验签算法：拒绝 alg 被篡改为 none / HS384 / RS256 等非预期值的令牌
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
        // 动态密钥解析：每次验签调用 secretProvider 取最新配置（优先于静态 IssuerSigningKey）
        IssuerSigningKeyResolver = secretProvider is null
            ? null
            : (_, _, _, _) => [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretProvider()))],
        ValidateLifetime = true,
        // 时钟偏移容忍 30 秒；claim 不做自动映射，保持 sub / unique_name / role 等原始名称
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "unique_name",
        RoleClaimType = "role"
    };

    /// <inheritdoc />
    public async Task<TokenPair> GenerateAsync(TokenSubject subject, string? ip = null, string? userAgent = null, CancellationToken cancellationToken = default, Guid? sessionId = null)
    {
        var now = DateTime.UtcNow;

        // 会话标识：登录签发时新建；刷新轮换时沿用旧会话标识，保证同一设备的 accessToken / refreshToken 共享一个 sid
        var sid = sessionId is { } existing && existing != Guid.Empty ? existing : Guid.NewGuid();
        var claims = BuildClaims(subject, sid);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.Secret)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: Options.Issuer,
            audience: Options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(Options.AccessTokenMinutes),
            signingCredentials: credentials);

        var accessToken = TokenHandler.WriteToken(jwt);

        // refreshToken：64 位十六进制 = 32 位会话标识 + 32 位随机密钥（128 位熵），仅以 SHA-256 哈希持久化；
        // 会话标识与 accessToken 的 sid 声明一致，支持按设备吊销而不影响同用户其他会话
        var refreshToken = sid.ToString("N") + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await refreshTokenRepository.InsertAsync(new SysRefreshToken
        {
            Token = RefreshTokenHasher.Hash(refreshToken),
            UserId = subject.UserId,
            ExpiresAt = now.AddDays(Options.RefreshTokenDays),
            Ip = ip,
            UserAgent = userAgent,
            SessionId = sid
        }, cancellationToken);

        return new TokenPair(accessToken, refreshToken, Options.AccessTokenMinutes * 60L);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateAccessToken(string accessToken)
    {
        try
        {
            return TokenHandler.ValidateToken(accessToken, BuildValidationParameters(Options), out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            // 签名无效、过期、格式错误等统一返回 null，由调用方决定 401 响应
            // 记录失败原因（不含令牌本身，避免敏感信息入日志），便于排查鉴权问题
            logger.LogWarning(exception, "accessToken 校验失败：{Reason}", exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RefreshTokenInfo?> GetActiveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var entity = await refreshTokenRepository.GetActiveByTokenAsync(RefreshTokenHasher.Hash(refreshToken), DateTime.UtcNow, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return new RefreshTokenInfo(entity.Id, entity.Token, entity.UserId, entity.ExpiresAt, entity.SessionId ?? Guid.Empty);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var entity = await refreshTokenRepository.GetActiveByTokenAsync(RefreshTokenHasher.Hash(refreshToken), DateTime.UtcNow, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.RevokedAt = DateTime.UtcNow;
        await refreshTokenRepository.UpdateAsync(entity, cancellationToken);
    }

    /// <summary>构造 accessToken claims（未认证 claims 如 jti 不参与业务，略去）。</summary>
    private List<Claim> BuildClaims(TokenSubject subject, Guid sessionId)
    {
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString()),
            new("unique_name", subject.UserName),
            // 用户级令牌版本与会话标识：请求期由 TokenValidationGuard 与缓存中的当前值比对，实现令牌可撤销
            new("tokenVersion", subject.TokenVersion.ToString()),
            new("sid", sessionId.ToString())
        ];

        if (subject.DeptId.HasValue)
        {
            claims.Add(new Claim("deptId", subject.DeptId.Value.ToString()));
        }

        claims.AddRange(subject.RoleIds.Select(roleId => new Claim("roleId", roleId.ToString())));
        claims.AddRange(subject.RoleKeys.Select(roleKey => new Claim("role", roleKey)));

        if (subject.TenantId.HasValue && subject.TenantId.Value != Guid.Empty)
        {
            claims.Add(new Claim("tenantId", subject.TenantId.Value.ToString()));
        }

        return claims;
    }
}
