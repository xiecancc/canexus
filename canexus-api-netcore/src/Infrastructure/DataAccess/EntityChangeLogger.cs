
using System.Collections.Concurrent;
using System.Reflection;
using Domain.Entities;

namespace Infrastructure.DataAccess;
/// <summary>
/// 实体变更记录器：对比聚合更新前后的属性快照，生成字段级（old / new）变更日志集合。
/// <para>审计与并发控制字段（Id / Version / CreatedAt / CreatedBy / UpdatedAt / UpdatedBy / IsDeleted）
/// 不参与对比；字段值格式化为字符串（null → null，超长截断至 1000 字符与列宽一致）。</para>
/// </summary>
internal static class EntityChangeLogger
{
    /// <summary>不参与变更对比的字段名（主键 / 乐观锁版本 / 创建与更新审计 / 软删除标记）。</summary>
    private static readonly string[] ExcludedProperties =
    [
        nameof(AggregateBase.Id),
        nameof(AggregateBase.Version),
        nameof(AggregateBase.CreatedAt),
        nameof(AggregateBase.CreatedBy),
        nameof(AggregateBase.UpdatedAt),
        nameof(AggregateBase.UpdatedBy),
        nameof(AggregateBase.IsDeleted)
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
        foreach (var property in PropertyCache.GetOrAdd(entityType, static type => [.. type
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && !ExcludedProperties.Contains(p.Name))]))
        {
            var oldValue = property.GetValue(oldEntity);
            var newValue = property.GetValue(newEntity);
            if (Equals(oldValue, newValue))
            {
                continue;
            }

            logs.Add(new SysEntityChangeLog
            {
                EntityName = entityType.Name,
                EntityId = newEntity.Id,
                FieldName = property.Name,
                OldValue = Format(oldValue),
                NewValue = Format(newValue),
                ChangedAt = changedAt,
                ChangedBy = changedBy
            });
        }

        return logs;
    }

    /// <summary>格式化字段值用于存储：null 保持 null，其余取 ToString 并截断超长。</summary>
    /// <param name="value">原始字段值。</param>
    /// <returns>存储字符串。</returns>
    private static string? Format(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.ToString();
        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] : text;
    }
}
