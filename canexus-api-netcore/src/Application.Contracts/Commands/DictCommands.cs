
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;

namespace Application.Contracts.Commands;
/// <summary>创建字典数据命令（同字典类型下标签 / 键值唯一，写后失效类型缓存）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record DictDataCreateCommand : AggregateCreateCommand<DictDataInputRequest>
{
    public DictDataCreateCommand(DictDataInputRequest request) : base(request) { }
}

/// <summary>创建字典类型命令（dictType 全局唯一）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record DictTypeCreateCommand : AggregateCreateCommand<DictTypeInputRequest>
{
    public DictTypeCreateCommand(DictTypeInputRequest request) : base(request) { }
}

/// <summary>删除字典数据命令（失效所属类型缓存）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record DictDataDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复字典数据命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record DictDataRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>删除字典类型命令（存在字典数据时拒绝，保证不留孤儿数据）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record DictTypeDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复字典类型命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record DictTypeRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新字典数据命令（类型可迁移，迁移后新旧类型缓存均失效）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">更新请求。</param>
public sealed record DictDataUpdateCommand : AggregateUpdateCommand<DictDataInputRequest>
{
    public DictDataUpdateCommand(Guid id, Guid version, DictDataInputRequest request) : base(id, version, request) { }
}

/// <summary>更新字典类型命令（dictType 不可变更）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">更新请求。</param>
public sealed record DictTypeUpdateCommand : AggregateUpdateCommand<DictTypeInputRequest>
{
    public DictTypeUpdateCommand(Guid id, Guid version, DictTypeInputRequest request) : base(id, version, request) { }
}
