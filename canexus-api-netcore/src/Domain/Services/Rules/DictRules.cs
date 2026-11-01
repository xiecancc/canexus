namespace Domain.Services.Rules;

/// <summary>字典模块缓存键构造（非校验规则）：写操作后按类型键失效字典数据缓存。</summary>
public static class DictRules
{
    /// <summary>字典数据缓存 key 前缀。</summary>
    public const string DataCacheKeyPrefix = "dict:data:";

    /// <summary>按字典类型构造数据缓存 key。</summary>
    /// <param name="dictType">字典类型。</param>
    /// <returns>缓存 key。</returns>
    public static string DataTypeCacheKey(string dictType) => DataCacheKeyPrefix + dictType.Trim();
}
