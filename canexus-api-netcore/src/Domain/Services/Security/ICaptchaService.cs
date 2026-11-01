namespace Domain.Services.Security;

/// <summary>
/// 图形验证码服务端口：生成（写入缓存）与一次性校验（读取后立即失效）。
/// <para>实现位于 Infrastructure（SVG 自绘 + HybridCache 存储，TTL 采用 Hot 档：
/// L1 30 秒 / L2 5 分钟——本实例校验走内存，分布式部署下跨实例经 Redis 校验）。</para>
/// </summary>
public interface ICaptchaService
{
    /// <summary>验证码缓存 key 前缀（key 格式：{prefix}{captchaId}）。</summary>
    public const string CacheKeyPrefix = "auth:captcha:";

    /// <summary>生成验证码并写入缓存（等待写入完成后返回，保证读到自己写的值）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>验证码 Id 与图片（SVG base64）。</returns>
    public ValueTask<CaptchaImage> GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>校验验证码（一次性：无论命中与否均移除，防止重放；大小写不敏感）。</summary>
    /// <param name="captchaId">验证码 Id。</param>
    /// <param name="code">用户输入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验通过返回 true。</returns>
    public ValueTask<bool> ValidateAsync(string captchaId, string code, CancellationToken cancellationToken = default);
}

/// <summary>验证码图片：Id 与 SVG base64 内容。</summary>
/// <param name="CaptchaId">验证码 Id（登录时回传）。</param>
/// <param name="ImageBase64">验证码图片（格式：data:image/svg+xml;base64,...）。</param>
public sealed record CaptchaImage(string CaptchaId, string ImageBase64);
