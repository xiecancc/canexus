
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Domain.Entities;
using Domain.Shared.Logging;

namespace Infrastructure.DataAccess;
/// <summary>
/// 实体变更记录器：对比聚合更新前后的属性快照，生成字段级（old / new）变更日志集合。
/// <para>审计与并发控制字段（Id / Version / CreatedAt / CreatedBy / UpdatedAt / UpdatedBy / DeletedAt）
/// 不参与对比；字段值格式化为字符串（null → null，超长截断至 1000 字符与列宽一致）。</para>
/// </summary>
internal static class EntityChangeLogger
{
    /// <summary>不参与变更对比的字段名（主键 / 乐观锁版本 / 创建与更新审计 / 软删除标记与唯一键槽）。</summary>
    private static readonly string[] ExcludedProperties =
    [
        nameof(AggregateBase.Id),
        nameof(AggregateBase.Version),
        nameof(AggregateBase.CreatedAt),
        nameof(AggregateBase.CreatedBy),
        nameof(AggregateBase.UpdatedAt),
        nameof(AggregateBase.UpdatedBy),
        nameof(AggregateBase.DeletedAt),
        nameof(AggregateBase.DeletedKey)
    ];

    /// <summary>实体可对比属性缓存（键为实体类型；属性集运行期不变，静态缓存避免重复反射）。</summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> PropertyCache = new();

    /// <summary>变更值最大存储长度（与 OldValue / NewValue 列宽一致，超长截断）。</summary>
    private const int MaxValueLength = 1000;

    /// <summary>对比更新前后的实体快照，生成字段级变更日志（仅包含值发生变化的业务字段）。</summary>
    /// <param name="oldEntity">更新前的数据库快照（null 表示行不存在，不生成日志）。</param>
    /// <param name="newEntity">更新后的实体。</param>
    /// <param name="changedAt">变更时间。</param>
    /// <param name="changedBy">变更人（无登录上下文时为 null）。</param>
    /// <returns>变更日志集合；无可记录变更（行不存在或无字段变化）时为空集合。</returns>
    public static List<SysEntityChangeLog> Build(AggregateBase? oldEntity, AggregateBase newEntity, DateTime changedAt, Guid? changedBy)
    {
        var logs = new List<SysEntityChangeLog>();
        if (oldEntity is null)
        {
            return logs;
        }

        var entityType = newEntity.GetType();
        foreach (var property in GetComparableProperties(entityType))
        {
            var oldValue = property.GetValue(oldEntity);
            var newValue = property.GetValue(newEntity);
            if (Equals(oldValue, newValue))
            {
                continue;
            }

            var sensitive = SensitiveDataMasker.IsSensitiveField(property.Name);
            logs.Add(new SysEntityChangeLog
            {
                EntityName = entityType.Name,
                EntityId = newEntity.Id,
                FieldName = property.Name,
                OldValue = Format(oldValue, sensitive),
                NewValue = Format(newValue, sensitive),
                ChangedAt = changedAt,
                ChangedBy = changedBy
            });
        }

        return logs;
    }

    /// <summary>为新插入的聚合生成逐字段「新增」日志（OldValue 为 null，敏感字段值脱敏）。</summary>
    /// <param name="entity">新插入的实体。</param>
    /// <param name="changedAt">变更时间。</param>
    /// <param name="changedBy">变更人（无登录上下文时为 null）。</param>
    /// <returns>变更日志集合。</returns>
    public static List<SysEntityChangeLog> BuildCreated(AggregateBase entity, DateTime changedAt, Guid? changedBy)
    {
        var logs = new List<SysEntityChangeLog>();
        var entityType = entity.GetType();
        foreach (var property in GetComparableProperties(entityType))
        {
            var value = property.GetValue(entity);
            if (value is null || value is string text && text.Length == 0)
            {
                continue;
            }

            logs.Add(new SysEntityChangeLog
            {
                EntityName = entityType.Name,
                EntityId = entity.Id,
                FieldName = property.Name,
                OldValue = null,
                NewValue = Format(value, SensitiveDataMasker.IsSensitiveField(property.Name)),
                ChangedAt = changedAt,
                ChangedBy = changedBy
            });
        }

        return logs;
    }

    /// <summary>为软删除 / 恢复生成字段级 DeletedAt 变更日志（旧值 / 新值为可空时间戳）。</summary>
    /// <param name="entityType">聚合实体类型。</param>
    /// <param name="entityId">实体主键。</param>
    /// <param name="oldDeletedAt">变更前的软删除时间戳（null 表示未删除）。</param>
    /// <param name="newDeletedAt">变更后的软删除时间戳（null 表示已恢复）。</param>
    /// <param name="changedAt">变更时间。</param>
    /// <param name="changedBy">变更人（无登录上下文时为 null）。</param>
    /// <returns>单字段变更日志。</returns>
    public static SysEntityChangeLog BuildLifecycleChange(
        Type entityType,
        Guid entityId,
        DateTime? oldDeletedAt,
        DateTime? newDeletedAt,
        DateTime changedAt,
        Guid? changedBy)
    {
        return new SysEntityChangeLog
        {
            EntityName = entityType.Name,
            EntityId = entityId,
            FieldName = nameof(AggregateBase.DeletedAt),
            OldValue = FormatTimestamp(oldDeletedAt),
            NewValue = FormatTimestamp(newDeletedAt),
            ChangedAt = changedAt,
            ChangedBy = changedBy
        };
    }

    /// <summary>时间戳按 ISO 8601 往返格式记录；null 保持 null（表示该状态下无时间戳）。</summary>
    /// <param name="value">时间戳。</param>
    /// <returns>格式化字符串或 null。</returns>
    private static string? FormatTimestamp(DateTime? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>取实体可对比属性（带类型缓存）。</summary>
    /// <param name="entityType">实体类型。</param>
    /// <returns>属性列表。</returns>
    private static IReadOnlyList<PropertyInfo> GetComparableProperties(Type entityType)
    {
        return PropertyCache.GetOrAdd(entityType, static type => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !ExcludedProperties.Contains(p.Name))]);
    }

    /// <summary>格式化字段值用于存储：null 保持 null，敏感字段替换为掩码，其余取 ToString 并截断超长。</summary>
    /// <param name="value">原始字段值。</param>
    /// <param name="sensitive">字段名是否命中脱敏规则。</param>
    /// <returns>存储字符串。</returns>
    private static string? Format(object? value, bool sensitive)
    {
        if (value is null)
        {
            return null;
        }

        if (sensitive)
        {
            return SensitiveDataMasker.MaskText;
        }

        var text = value.ToString();
        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] : text;
    }
}
