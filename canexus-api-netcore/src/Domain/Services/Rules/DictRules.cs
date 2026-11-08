namespace Domain.Services.Rules;

/// <summary>字典模块缓存键构造（非校验规则）：写操作后按类型键失效字典数据缓存。</summary>
public static class DictRules
{
    /// <summary>字典数据缓存 key 前缀。</summary>
    public const string DataCacheKeyPrefix = "dict:data:";

    /// <summary>
    /// 按字典类型构造数据缓存 key：平台范围（tenantId 为 null）沿用历史键名；
    /// 租户范围追加 <c>:tenant:{id}</c>，避免 A 租户的字典数据被 B 租户命中。
    /// </summary>
    /// <param name="dictType">字典类型。</param>
    /// <param name="tenantId">当前租户 Id；null 表示平台范围。</param>
    /// <returns>缓存 key。</returns>
    public static string DataTypeCacheKey(string dictType, Guid? tenantId = null)
    {
        var key = DataCacheKeyPrefix + dictType.Trim();
        return tenantId is { } id ? $"{key}:tenant:{id:N}" : key;
    }
}
