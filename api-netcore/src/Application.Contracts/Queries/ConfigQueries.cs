namespace Application.Contracts.Queries;

using Application.Contracts.Abstractions;
using Application.Contracts.Configs;
using Domain.Shared.Paging;

/// <summary>按参数键名查询参数值（走缓存，写操作后立即刷新）。</summary>
/// <param name="ConfigKey">参数键名。</param>
public sealed record GetConfigByKeyQuery(string ConfigKey) : IQuery<string>;

/// <summary>分页查询参数配置（keyword 模糊匹配名称 / 键名 / 键值）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetConfigPageQuery(ConfigPageRequest Request) : IQuery<PagedResult<ConfigListVO>>;
