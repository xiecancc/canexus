namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;

/// <summary>创建字典数据命令（同字典类型下标签 / 键值唯一，写后失效类型缓存）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateDictDataCommand(DictDataCreateRequest Request) : ICommand<Guid>;

/// <summary>创建字典类型命令（dictType 全局唯一）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateDictTypeCommand(DictTypeCreateRequest Request) : ICommand<Guid>;

/// <summary>删除字典数据命令（失效所属类型缓存）。</summary>
/// <param name="Id">字典数据 Id。</param>
public sealed record DeleteDictDataCommand(Guid Id) : ICommand;

/// <summary>删除字典类型命令（存在字典数据时拒绝，保证不留孤儿数据）。</summary>
/// <param name="Id">字典类型 Id。</param>
public sealed record DeleteDictTypeCommand(Guid Id) : ICommand;

/// <summary>更新字典数据命令（类型可迁移，迁移后新旧类型缓存均失效）。</summary>
/// <param name="Id">字典数据 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateDictDataCommand(Guid Id, DictDataUpdateRequest Request) : ICommand;

/// <summary>更新字典类型命令（dictType 不可变更）。</summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateDictTypeCommand(Guid Id, DictTypeUpdateRequest Request) : ICommand;
