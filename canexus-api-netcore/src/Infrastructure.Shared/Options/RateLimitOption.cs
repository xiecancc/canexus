namespace Infrastructure.Shared.Options;

/// <summary>
/// 接口限流配置项（绑定配置节 RateLimit，基于 .NET 内置 RateLimiter）。
/// <para>限流按客户端 IP 分区计数；若部署在反向代理之后，需在宿主启用 ForwardedHeaders 中间件
/// 并将代理加入 KnownProxies，否则所有请求将共享代理 IP 的限流配额。</para>
/// </summary>
public sealed class RateLimitOption : OptionBase
{
    /// <summary>是否启用接口限流。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>全局限流：每分区每窗口允许的请求数（0 = 不启用全局限流；适用于全部端点）。</summary>
    public int GlobalPermitLimit { get; set; }

    /// <summary>全局限流窗口时长（秒），仅 GlobalPermitLimit &gt; 0 时生效。</summary>
    public int GlobalWindowSeconds { get; set; } = 10;

    /// <summary>认证端点限流：每分区每窗口允许的请求数（作用于验证码 / 登录 / 刷新令牌等匿名端点）。</summary>
    public int AuthPermitLimit { get; set; } = 100;

    /// <summary>认证端点限流窗口时长（秒）。</summary>
    public int AuthWindowSeconds { get; set; } = 60;

    /// <inheritdoc />
    public override void Validate()
    {
        // 未启用时不约束其余字段（开发/测试环境可整体关闭）
        if (!Enabled)
        {
            return;
        }

        Ensure(GlobalPermitLimit >= 0, nameof(GlobalPermitLimit), "RateLimit:GlobalPermitLimit 不允许为负数");
        EnsureGreaterThanZero(AuthPermitLimit, nameof(AuthPermitLimit), "启用限流时 RateLimit:AuthPermitLimit 必须大于 0");

        // 对应限流开启时窗口必须有效
        if (GlobalPermitLimit > 0)
        {
            EnsureGreaterThanZero(GlobalWindowSeconds, nameof(GlobalWindowSeconds), "GlobalPermitLimit 大于 0 时 RateLimit:GlobalWindowSeconds 必须大于 0");
        }

        EnsureGreaterThanZero(AuthWindowSeconds, nameof(AuthWindowSeconds), "RateLimit:AuthWindowSeconds 必须大于 0");
    }
}
