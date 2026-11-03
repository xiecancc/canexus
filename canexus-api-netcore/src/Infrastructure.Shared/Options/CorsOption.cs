namespace Infrastructure.Shared.Options;

/// <summary>CORS 跨域配置项（绑定配置节 Cors）。</summary>
public sealed class CorsOption : OptionBase
{
    /// <summary>是否启用 CORS 策略（生产环境接入前端域名时开启）。</summary>
    public bool Enabled { get; set; }

    /// <summary>允许的跨域来源白名单（精确匹配，禁止使用 "*"；与 AllowCredentials 组合时通配符会被浏览器拒绝）。</summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>允许的请求头（逗号分隔；默认包含认证、内容类型与租户标识头）。</summary>
    public string AllowedHeaders { get; set; } = "Authorization,Content-Type,X-Tenant-Id";

    /// <summary>允许的 HTTP 方法（逗号分隔）。</summary>
    public string AllowedMethods { get; set; } = "GET,POST,PUT,DELETE,OPTIONS";

    /// <summary>预检（OPTIONS）响应缓存时长（秒），减少浏览器重复预检。</summary>
    public int PreflightMaxAgeSeconds { get; set; } = 600;

    /// <inheritdoc />
    public override void Validate()
    {
        // 未启用时不约束其余字段（开发/测试环境可留空）
        if (!Enabled)
        {
            return;
        }

        EnsureNotEmpty(AllowedOrigins, nameof(AllowedOrigins), "启用 CORS 时 Cors:AllowedOrigins 不能为空集合");
        Ensure(AllowedOrigins.All(origin => !string.IsNullOrWhiteSpace(origin) && !origin.Equals("*", StringComparison.OrdinalIgnoreCase)),
            nameof(AllowedOrigins), "Cors:AllowedOrigins 不允许包含 \"*\"（与 AllowCredentials 组合时浏览器会拒绝，必须列出精确来源）");
        EnsureNotNullOrWhiteSpace(AllowedHeaders, nameof(AllowedHeaders), "启用 CORS 时 Cors:AllowedHeaders 不能为空");
        EnsureNotNullOrWhiteSpace(AllowedMethods, nameof(AllowedMethods), "启用 CORS 时 Cors:AllowedMethods 不能为空");
        EnsureGreaterThanZero(PreflightMaxAgeSeconds, nameof(PreflightMaxAgeSeconds), "Cors:PreflightMaxAgeSeconds 必须大于 0");
    }
}
