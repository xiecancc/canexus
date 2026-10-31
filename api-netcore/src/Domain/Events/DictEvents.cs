namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>字典类型变更领域事件：字典类型创建 / 更新 / 删除成功后发布（类型键名不可变更，无迁移语义）。</summary>
/// <param name="DictTypeId">变更的字典类型 Id。</param>
/// <param name="ChangeType">变更类型。</param>
public sealed class DictTypeChangedEvent(Guid dictTypeId, AggregateChangeType changeType)
    : AggregateChangedEvent("dict.type.changed", dictTypeId, changeType)
{
    /// <summary>变更的字典类型 Id。</summary>
    public Guid DictTypeId { get; } = dictTypeId;
}

/// <summary>字典数据变更领域事件：字典数据创建 / 更新（含类型迁移）/ 删除成功后发布。</summary>
/// <param name="DictDataId">变更的字典数据 Id。</param>
/// <param name="DictType">所属字典类型（迁移后）。</param>
/// <param name="ChangeType">变更类型。</param>
/// <param name="OriginalDictType">类型迁移前的旧类型；未迁移为 null。</param>
public sealed class DictDataChangedEvent(Guid dictDataId, string dictType, AggregateChangeType changeType, string? originalDictType = null)
    : AggregateChangedEvent("dict.data.changed", dictDataId, changeType, new Dictionary<string, object?>
    {
        ["dictType"] = dictType,
        ["originalDictType"] = originalDictType
    })
{
    /// <summary>变更的字典数据 Id。</summary>
    public Guid DictDataId { get; } = dictDataId;

    /// <summary>所属字典类型（迁移后）。</summary>
    public string DictType { get; } = dictType;

    /// <summary>类型迁移前的旧类型；未迁移为 null。</summary>
    public string? OriginalDictType { get; } = originalDictType;
}
