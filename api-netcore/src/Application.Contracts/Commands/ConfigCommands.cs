
using Application.Contracts.Abstractions;
using Application.Contracts.Configs;

namespace Application.Contracts.Commands;
/// <summary>创建参数配置命令（configKey 唯一，创建项一律非内置）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record ConfigCreateCommand : DomainCreateCommand<ConfigCreateRequest>
{
    public ConfigCreateCommand(ConfigCreateRequest request) : base(request) { }
}

/// <summary>删除参数配置命令（内置参数拒绝删除）。</summary>
/// <param name="Id">参数 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record ConfigDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>更新参数配置命令（键名可变更，新旧键缓存均失效；内置参数可编辑）。</summary>
/// <param name="Id">参数 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record ConfigUpdateCommand : AggregateUpdateCommand<ConfigUpdateRequest>
{
    public ConfigUpdateCommand(Guid id, ConfigUpdateRequest request) : base(id, request) { }
}
