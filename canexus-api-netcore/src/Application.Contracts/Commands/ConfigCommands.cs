
using Application.Contracts.Abstractions;
using Application.Contracts.Configs;

namespace Application.Contracts.Commands;
/// <summary>创建参数配置命令（configKey 唯一，创建项一律非内置）。</summary>
public sealed record ConfigCreateCommand : AggregateCreateCommand<ConfigInputRequest>
{
    /// <summary>构造创建参数配置命令。</summary>
    /// <param name="request">创建请求。</param>
    public ConfigCreateCommand(ConfigInputRequest request) : base(request) { }
}

/// <summary>删除参数配置命令（内置参数拒绝删除）。</summary>
/// <param name="Id">参数 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record ConfigDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复参数配置命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">参数 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record ConfigRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新参数配置命令（键名可变更，新旧键缓存均失效；内置参数可编辑）。</summary>
public sealed record ConfigUpdateCommand : AggregateUpdateCommand<ConfigInputRequest>
{
    /// <summary>构造更新参数配置命令。</summary>
    /// <param name="id">参数 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public ConfigUpdateCommand(Guid id, Guid version, ConfigInputRequest request) : base(id, version, request) { }
}
