
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;

namespace Application.Contracts.Commands;
/// <summary>创建字典数据命令（同字典类型下标签 / 键值唯一，写后失效类型缓存）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record DictDataCreateCommand : DomainCreateCommand<DictDataCreateRequest>
{
    public DictDataCreateCommand(DictDataCreateRequest request) : base(request) { }
}

/// <summary>创建字典类型命令（dictType 全局唯一）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record DictTypeCreateCommand : DomainCreateCommand<DictTypeCreateRequest>
{
    public DictTypeCreateCommand(DictTypeCreateRequest request) : base(request) { }
}

/// <summary>删除字典数据命令（失效所属类型缓存）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record DictDataDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>删除字典类型命令（存在字典数据时拒绝，保证不留孤儿数据）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record DictTypeDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>更新字典数据命令（类型可迁移，迁移后新旧类型缓存均失效）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record DictDataUpdateCommand : AggregateUpdateCommand<DictDataUpdateRequest>
{
    public DictDataUpdateCommand(Guid id, DictDataUpdateRequest request) : base(id, request) { }
}

/// <summary>更新字典类型命令（dictType 不可变更）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record DictTypeUpdateCommand : AggregateUpdateCommand<DictTypeUpdateRequest>
{
    public DictTypeUpdateCommand(Guid id, DictTypeUpdateRequest request) : base(id, request) { }
}
