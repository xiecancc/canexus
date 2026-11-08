
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace API.Extensions;
/// <summary>
/// 启动期配置基线校验：把「仓库默认配置只能用于本机开发」变成代码可验证的门禁。
/// <para>非 Development 环境校验密钥类配置；Production 环境额外校验传输 / 限流 / 多租户 / 转发头 /
/// L2 缓存等部署基线，并一次性汇总全部不合规项后阻止启动。</para>
/// </summary>
public static class ProductionConfigurationValidator
{
    /// <summary>按宿主环境执行配置基线校验，存在不合规项时抛出 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="configuration">宿主最终生效的配置（含环境变量与内存覆盖）。</param>
    /// <param name="environment">宿主环境。</param>
    /// <exception cref="InvalidOperationException">任一基线项不合规时抛出，消息中列出全部不合规项。</exception>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var issues = new List<string>();
        if (!environment.IsDevelopment())
        {
            ValidateSecrets(configuration, issues);
        }

        if (environment.IsProduction())
        {
            ValidateProductionBaseline(configuration, issues);
        }

        if (issues.Count == 0)
        {
            return;
        }

        var message = $"启动配置基线校验失败（{environment.EnvironmentName}）：\n- {string.Join("\n- ", issues)}";
        Log.Fatal("启动配置基线校验失败：{Issues}", string.Join("；", issues));
        throw new InvalidOperationException(message);
    }

    /// <summary>校验密钥类配置：不得为空、示例值或开发回退值，且内置超管口令必须由部署方显式提供。</summary>
    private static void ValidateSecrets(IConfiguration configuration, List<string> issues)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret)
            || string.Equals(secret, JwtOption.InsecureDefaultSecret, StringComparison.Ordinal)
            || string.Equals(secret, JwtOption.DevelopmentFallbackSecret, StringComparison.Ordinal))
        {
            issues.Add("Jwt:Secret 必须显式提供独立随机密钥（不得为空、示例值或 Development 回退值）");
        }

        if (string.IsNullOrWhiteSpace(configuration["Bootstrap:AdminInitialPassword"]))
        {
            issues.Add("Bootstrap:AdminInitialPassword 必须显式提供内置超管初始口令");
        }
    }

    /// <summary>校验 Production 部署基线：安全传输、业务限流、显式声明的租户与缓存策略、可信代理白名单。</summary>
    private static void ValidateProductionBaseline(IConfiguration configuration, List<string> issues)
    {
        var requireChange = configuration["Bootstrap:RequirePasswordChangeOnSeed"];
        if (requireChange is not null && bool.TryParse(requireChange, out var required) && !required)
        {
            issues.Add("Bootstrap:RequirePasswordChangeOnSeed 在 Production 必须为 true（种子 admin 首登须改密）");
        }

        // 生产不得由应用自动改表：结构变更须经迁移通道的幂等脚本 + 版本状态机，
        // 已就绪的启动完全不执行 DDL（见 DbInitializer 三层判定）
        if (configuration.GetValue("Database:AutoCreateTable", true))
        {
            issues.Add("Database:AutoCreateTable 在 Production 必须显式配置为 false（禁止应用自动建表 / 重建表结构）");
        }

        if (configuration.GetValue("Database:AutoSyncStructure", true))
        {
            issues.Add("Database:AutoSyncStructure 在 Production 必须显式配置为 false（禁止应用自动同步表结构）");
        }

        if (!configuration.GetValue("Https:RedirectEnabled", false))
        {
            issues.Add("Https:RedirectEnabled 必须为 true（强制 HTTPS 重定向）");
        }

        if (!configuration.GetValue("Https:HstsEnabled", false))
        {
            issues.Add("Https:HstsEnabled 必须为 true");
        }

        if (!configuration.GetValue("RateLimit:Enabled", false)
            || configuration.GetValue("RateLimit:GlobalPermitLimit", 0) <= 0)
        {
            issues.Add("RateLimit:Enabled=true 且 RateLimit:GlobalPermitLimit>0（业务端点须受全局限流）");
        }

        var multiTenancy = configuration["MultiTenancy:Enabled"];
        if (string.IsNullOrWhiteSpace(multiTenancy) || !bool.TryParse(multiTenancy, out _))
        {
            issues.Add("MultiTenancy:Enabled 必须显式声明 true / false，不得依赖隐式默认值");
        }

        if (configuration.GetValue("Forwarded:Enabled", false)
            && string.IsNullOrWhiteSpace(configuration["Forwarded:KnownProxies"]))
        {
            issues.Add("Forwarded:Enabled=true 时必须配置 Forwarded:KnownProxies 可信代理白名单");
        }

        var redisEnabled = configuration["Cache:RedisEnabled"];
        if (string.IsNullOrWhiteSpace(redisEnabled) || !bool.TryParse(redisEnabled, out _))
        {
            issues.Add("Cache:RedisEnabled 必须显式声明 L2 缓存提供方式（开启或关闭均须显式）");
        }
    }
}
