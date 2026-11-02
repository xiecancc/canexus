using Application.Contracts.Dtos.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Configs;
/// <summary>参数配置分页查询请求（GET /api/system/config/page）。</summary>
public class ConfigPageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配参数名称 / 参数键名 / 参数键值。</summary>
    public string? Keyword { get; set; }
}

/// <summary>参数配置列表行（分页数据项）。</summary>
public class ConfigPagedResponse : AggregatePagedResponse
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

/// <summary>参数配置写入请求（POST / PUT 共用载荷；对齐 SysConfig 聚合；该聚合无状态字段，创建的参数一律为非内置）。</summary>
public class ConfigInputRequest : AggregateInputRequest
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
