using System.Text.Json;
using Domain.Events;
using Domain.Shared.Json;

namespace Application.Events;

/// <summary>
/// Outbox 载荷序列化器：领域事件 ↔ JSON（System.Text.Json，复用 <see cref="JsonHelper.WebOptions"/> 的
/// camelCase / 中文不转义统一契约，敏感字段由实体侧 [JsonIgnore] 排除）。
/// <para>T-16：不再自建第二套 <see cref="JsonSerializerOptions"/>（旧实现为默认 PascalCase，
/// 与 HTTP 响应、日志及 <see cref="JsonHelper"/> 的命名口径不一致，字典 / 枚举载荷存在往返风险）。
/// 统一后新载荷统一 camelCase；<see cref="JsonHelper.WebOptions"/> 的
/// <c>PropertyNameCaseInsensitive</c> 保证存量 PascalCase 载荷仍可重放。</para>
/// <para>P1-R3 健壮化：事件构造函数参数名与属性名统一（见 DomainEvent / AggregateEvents 基类族约定）、
/// <see cref="DomainEvent.OccurredAt"/> 为 init 属性，标准 STJ 构造绑定即可完整往返——
/// 移除旧实现的构造函数参数名魔法匹配（before→OldEntity 等语义映射）与后备字段反射，
/// 事件类重构（如重命名参数）不再静默破坏重放。</para>
/// </summary>
public static class OutboxEventSerializer
{
    /// <summary>允许反序列化的领域事件命名空间（拒绝任意 CLR 类型）。</summary>
    private const string EventNamespace = "Domain.Events";

    /// <summary>序列化领域事件为 JSON 载荷。</summary>
    /// <param name="domainEvent">领域事件。</param>
    /// <returns>JSON 字符串。</returns>
    public static string Serialize(DomainEvent domainEvent)
    {
        return JsonHelper.Serialize(domainEvent, domainEvent.GetType());
    }

    /// <summary>
    /// 反序列化 JSON 载荷为领域事件（按事件类型名解析 CLR 类型，类型不存在或载荷不合法时抛出）。
    /// <para>经标准 STJ 构造绑定还原：事件属性与构造函数参数名一致（基类族命名契约保证），
    /// <see cref="DomainEvent.OccurredAt"/> 经 init 访问器由载荷还原。</para>
    /// </summary>
    /// <param name="eventType">事件 CLR 完整类型名（如 Domain.Events.UserPasswordResetEvent）。</param>
    /// <param name="payload">JSON 载荷。</param>
    /// <returns>反序列化后的领域事件。</returns>
    /// <exception cref="InvalidOperationException">事件类型不属于允许的领域事件、不存在或载荷无法反序列化时抛出（投递侧记录失败并重试）。</exception>
    public static DomainEvent Deserialize(string eventType, string payload)
    {
        var type = ResolveType(eventType)
            ?? throw new InvalidOperationException($"Outbox 事件类型不存在或不是允许的领域事件类型：{eventType}");

        try
        {
            return JsonHelper.Deserialize(payload, type) as DomainEvent
                ?? throw new InvalidOperationException($"Outbox 载荷反序列化结果为空：{eventType}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Outbox 载荷反序列化失败：{eventType}：{exception.Message}", exception);
        }
    }

    /// <summary>
    /// 解析事件类型：优先在 <see cref="DomainEvent"/> 所在程序集按 FullName 精确查找，
    /// 再扫描其余已加载程序集；仅接受 Domain.Events 命名空间下的具体 <see cref="DomainEvent"/> 子类，
    /// 拒绝任意 CLR 类型（防止载荷被反序列化为非事件类型而产生构造副作用）。
    /// </summary>
    /// <param name="eventType">事件 CLR 完整类型名。</param>
    /// <returns>允许的领域事件类型；未找到或不允许时返回 null。</returns>
    private static Type? ResolveType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 512)
        {
            return null;
        }

        var contractAssembly = typeof(DomainEvent).Assembly;
        var declared = contractAssembly.GetType(eventType, throwOnError: false);
        if (IsAllowedEventType(declared))
        {
            return declared;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == contractAssembly || assembly.IsDynamic)
            {
                continue;
            }

            var type = assembly.GetType(eventType, throwOnError: false);
            if (IsAllowedEventType(type))
            {
                return type;
            }
        }

        return null;

        static bool IsAllowedEventType(Type? type)
        {
            return type is not null
                && type.IsClass
                && !type.IsAbstract
                && typeof(DomainEvent).IsAssignableFrom(type)
                && (type.Namespace?.Equals(EventNamespace, StringComparison.Ordinal) == true
                    || type.Namespace?.StartsWith(EventNamespace + ".", StringComparison.Ordinal) == true);
        }
    }
}
