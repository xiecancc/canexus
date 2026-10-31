namespace Domain.Events;

using Domain.Entities;

/// <summary>参数配置创建领域事件：配置创建成功后发布，携带创建后的状态。</summary>
/// <param name="Entity">创建后的参数配置。</param>
public sealed class ConfigCreatedEvent(SysConfig entity) : CreatedEvent<SysConfig>("config.created", entity);

/// <summary>参数配置修改领域事件：配置更新成功后发布，携带变更前后状态——键名变更时旧键名由 Before.ConfigKey 表达，监听器可据此双失效缓存。</summary>
/// <param name="Before">变更前的配置快照。</param>
/// <param name="After">变更后的配置。</param>
public sealed class ConfigUpdatedEvent(SysConfig before, SysConfig after) : UpdatedEvent<SysConfig>("config.updated", before, after);

/// <summary>参数配置删除领域事件：配置删除成功后发布，携带删除前的状态（内置参数受保护不可删除）。</summary>
/// <param name="Entity">删除前的参数配置。</param>
public sealed class ConfigDeletedEvent(SysConfig entity) : DeletedEvent<SysConfig>("config.deleted", entity);
