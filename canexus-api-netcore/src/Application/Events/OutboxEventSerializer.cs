using System.Text.Json;
using Domain.Events;

namespace Application.Events;

/// <summary>
/// Outbox 载荷序列化器：领域事件 ↔ JSON（System.Text.Json，PascalCase 契约，敏感字段由实体侧 [JsonIgnore] 排除）。
/// <para>P1-R3 健壮化：事件构造函数参数名与属性名统一（见 DomainEvent / AggregateEvents 基类族约定）、
/// <see cref="DomainEvent.OccurredAt"/> 为 init 属性，标准 STJ 构造绑定即可完整往返——
/// 移除旧实现的构造函数参数名魔法匹配（before→OldEntity 等语义映射）与后备字段反射，
/// 事件类重构（如重命名参数）不再静默破坏重放。</para>
/// </summary>
public static class OutboxEventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // 事件为不可变类（get-only 属性 + 构造函数赋值）：反序列化走 STJ 构造绑定
        //（参数名与属性名大小写不敏感匹配），嵌套实体（Sys* POCO）按声明类型还原
        WriteIndented = false
    };

    /// <summary>序列化领域事件为 JSON 载荷。</summary>
    /// <param name="domainEvent">领域事件。</param>
    /// <returns>JSON 字符串。</returns>
    public static string Serialize(DomainEvent domainEvent)
    {
        return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);
    }

    /// <summary>
    /// 反序列化 JSON 载荷为领域事件（按事件类型名解析 CLR 类型，类型不存在或载荷不合法时抛出）。
    /// <para>经标准 STJ 构造绑定还原：事件属性与构造函数参数名一致（基类族命名契约保证），
    /// <see cref="DomainEvent.OccurredAt"/> 经 init 访问器由载荷还原。</para>
    /// </summary>
    /// <param name="eventType">事件 CLR 完整类型名（如 Domain.Events.UserPasswordResetEvent）。</param>
    /// <param name="payload">JSON 载荷。</param>
    /// <returns>反序列化后的领域事件。</returns>
    /// <exception cref="InvalidOperationException">事件类型不存在或载荷无法反序列化时抛出（投递侧记录失败并重试）。</exception>
    public static DomainEvent Deserialize(string eventType, string payload)
    {
        var type = ResolveType(eventType)
            ?? throw new InvalidOperationException($"Outbox 事件类型不存在：{eventType}");

        try
        {
            return JsonSerializer.Deserialize(payload, type, Options) as DomainEvent
                ?? throw new InvalidOperationException($"Outbox 载荷反序列化结果为空：{eventType}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Outbox 载荷反序列化失败：{eventType}：{exception.Message}", exception);
        }
    }

    /// <summary>在已加载程序集中解析事件类型（存储使用 FullName，故须扫描全部程序集而非仅当前程序集）。</summary>
    /// <param name="eventType">事件 CLR 完整类型名。</param>
    /// <returns>匹配的类型；未找到返回 null。</returns>
    private static Type? ResolveType(string eventType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(eventType, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}
