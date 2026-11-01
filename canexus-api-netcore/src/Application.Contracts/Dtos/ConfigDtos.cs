
using Application.Contracts.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Configs;
/// <summary>参数配置分页查询请求（GET /api/system/config/page）。</summary>
public class ConfigPageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配参数名称 / 参数键名 / 参数键值。</summary>
    public string? Keyword { get; set; }
}

/// <summary>参数配置列表行（分页数据项）。</summary>
public class ConfigListResponse : AggregatePagedResponse
{
    /// <summary>参数名称。</summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>参数键名（唯一）。</summary>
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>参数键值。</summary>
    public string ConfigValue { get; set; } = string.Empty;

    /// <summary>是否内置参数（内置参数不允许删除）。</summary>
    public bool BuiltIn { get; set; }

}

/// <summary>参数配置写入请求基类：创建 / 更新共同字段（对齐 SysConfig 聚合；该聚合无状态字段，创建的参数一律为非内置；挂接创建请求根）。</summary>
public abstract class ConfigRequest : AggregateChangeRequest
{
    /// <summary>参数名称。</summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>参数键名（唯一，业务层校验重复）。</summary>
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>参数键值。</summary>
    public string ConfigValue { get; set; } = string.Empty;

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>创建参数配置请求（POST /api/system/config；创建的参数一律为非内置）。</summary>
public class ConfigCreateRequest : ConfigRequest
{
}

/// <summary>更新参数配置请求（PUT /api/system/config/{id}；内置参数可编辑但不可删除；Version 须回传乐观锁版本号）。</summary>
public class ConfigUpdateRequest : ConfigRequest
{
    /// <summary>乐观锁版本号（须与服务端聚合当前版本一致才允许修改）。</summary>
    public Guid Version { get; set; }
}

/// <summary>删除参数配置请求（DELETE /api/system/config/{id}；Version 须回传乐观锁版本号）。</summary>
public class ConfigDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
}
