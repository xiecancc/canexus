namespace Domain.Services.Rules;

/// <summary>参数配置模块缓存键构造（非校验规则）：写操作后按键失效参数值缓存。键名格式校验已迁移至 Validators 模块。</summary>
public static class ConfigRules
{
    /// <summary>参数键名最大长度（与实体列长度一致，供 Validators 文案引用）。</summary>
    public const int KeyMaxLength = 100;

    /// <summary>参数值缓存 key 前缀。</summary>
    public const string ValueCacheKeyPrefix = "config:key:";

    /// <summary>按参数键名构造值缓存 key。</summary>
    /// <param name="configKey">参数键名。</param>
    /// <returns>缓存 key。</returns>
    public static string ValueCacheKey(string configKey) => ValueCacheKeyPrefix + configKey.Trim();
}
