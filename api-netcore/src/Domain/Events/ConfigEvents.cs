namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>参数配置变更领域事件：配置创建 / 更新（含键名变更）/ 删除成功后发布。</summary>
/// <param name="ConfigId">变更的参数配置 Id。</param>
/// <param name="ConfigKey">参数键名（变更后）。</param>
/// <param name="ChangeType">变更类型。</param>
/// <param name="OriginalConfigKey">键名变更前的旧键名；未变更为 null。</param>
public sealed class ConfigChangedEvent(Guid configId, string configKey, AggregateChangeType changeType, string? originalConfigKey = null)
    : AggregateChangedEvent("config.changed", configId, changeType, new Dictionary<string, object?>
    {
        ["configKey"] = configKey,
        ["originalConfigKey"] = originalConfigKey
    })
{
    /// <summary>变更的参数配置 Id。</summary>
    public Guid ConfigId { get; } = configId;

    /// <summary>参数键名（变更后）。</summary>
    public string ConfigKey { get; } = configKey;

    /// <summary>键名变更前的旧键名；未变更为 null。</summary>
    public string? OriginalConfigKey { get; } = originalConfigKey;
}
