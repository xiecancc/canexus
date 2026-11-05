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

    /// <summary>是否使用 Redis 分布式限流：多副本共享同一 Redis 计数键，实现跨实例一致的限流配额（P1-A5）。
    /// <para>false（默认，开发 / 单副本）时为进程内限流；true 时需提供 <see cref="RedisConfiguration"/>，
    /// 生产多副本部署建议开启（见部署文档 docker-compose）。</para>
    /// </summary>
    public bool UseRedis { get; set; }

    /// <summary>Redis 连接串（StackExchange.Redis 格式），仅 <see cref="UseRedis"/> 为 true 时生效。</summary>
    public string RedisConfiguration { get; set; } = "localhost:6379";

    /// <summary>限流 Redis 键前缀（多应用 / 多环境共享 Redis 时隔离命名空间）。</summary>
    public string RedisKeyPrefix { get; set; } = "super-admin:rl:";

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

        // Redis 分布式限流仅在显式开启时校验相关配置
        if (UseRedis)
        {
            EnsureNotNullOrWhiteSpace(RedisConfiguration, nameof(RedisConfiguration), "RateLimit:UseRedis=true 时 RateLimit:RedisConfiguration 不能为空");
            EnsureNotNullOrWhiteSpace(RedisKeyPrefix, nameof(RedisKeyPrefix), "RateLimit:RedisKeyPrefix 不能为空");
        }
    }
}
