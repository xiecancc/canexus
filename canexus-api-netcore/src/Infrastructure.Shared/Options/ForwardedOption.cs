
using System.Net;

namespace Infrastructure.Shared.Options;

/// <summary>反向代理转发头配置项（绑定配置节 Forwarded）。</summary>
/// <para>部署在 Nginx / SLB 等反向代理之后时开启：中间件解析 X-Forwarded-For 等转发头，
/// 还原真实客户端 IP，供接口限流分区键与操作日志 OperIp 使用；未开启时保持直连语义。</para>
public sealed class ForwardedOption : OptionBase
{
    /// <summary>是否启用转发头解析（默认关闭；仅在可信反向代理之后开启，否则客户端可伪造头绕过限流）。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 要解析的转发头集合（逗号分隔，忽略大小写）：
    /// All（全部）/ XForwardedFor / XForwardedProto / XForwardedHost / XForwardedForProto。
    /// </summary>
    public string Headers { get; set; } = "All";

    /// <summary>已知代理 IP 白名单（逗号分隔；仅这些代理提交的转发头会被采信，本地回环地址默认始终受信）。</summary>
    public string KnownProxies { get; set; } = string.Empty;

    /// <inheritdoc />
    public override void Validate()
    {
        // 未启用时不校验其余字段（与 CorsOption / RateLimitOption 的开关语义一致）
        if (!Enabled)
        {
            return;
        }

        // Headers 的枚举值合法性在 API 层解析转发头时校验（ForwardedHeaders 枚举属于 ASP.NET 共享框架，
        // 不引入本层）；此处仅保证非空，未知值将在启动期 fail-fast
        EnsureNotNullOrWhiteSpace(Headers, nameof(Headers), "Forwarded:Headers 不能为空");

        // KnownProxies 必须是合法 IP（IPAddress 属于 BCL，可在此校验；空串跳过）
        var proxies = KnownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var proxy in proxies)
        {
            Ensure(IPAddress.TryParse(proxy, out _), nameof(KnownProxies), $"Forwarded:KnownProxies 含非法 IP 地址：{proxy}");
        }
    }
}
