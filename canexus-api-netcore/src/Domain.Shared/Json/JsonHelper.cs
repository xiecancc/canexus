
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Domain.Shared.Json;
/// <summary>
/// JSON 序列化工具（全应用统一入口）：收敛散落在各处的 JsonSerializerOptions，保证序列化行为全局一致。
/// <para>归属说明：本类仅依赖 BCL（System.Text.Json），无任何基础设施语义，故置于被全部层引用且自身
/// 零依赖的 <c>Domain.Shared</c>（共享内核），而非 <c>Infrastructure.Shared</c>（基础设施横切关注点）。</para>
/// <list type="bullet">
/// <item>统一配置（<see cref="WebOptions"/>）：Web 默认约定（camelCase 属性名、反序列化忽略大小写、字符串数字兼容读取）
/// 叠加中文不转义（UnsafeRelaxedJsonEscaping），HTTP 响应与日志中的中文保持可读；
/// T-16 起全应用唯一一份 JSON 配置——Outbox 载荷序列化与健康报告写出均复用本单例，
/// 不再自建 Options；忽略大小写的读取策略同时兼容存量 PascalCase Outbox 载荷；</item>
/// <item><see cref="Serialize{TValue}(TValue)"/> / <see cref="Deserialize{TValue}(string)"/>：标准版本，
/// 非法输入抛 JsonException，由调用方按自身异常策略处理（请求链路上的异常统一进入全局异常处理）；</item>
/// <item><see cref="TryDeserialize{TValue}"/>：容错版本，解析失败返回 false 不抛异常，
/// 适用于日志、外部系统输入等不可信数据的防御性解析。</item>
/// </list>
/// </summary>
public static class JsonHelper
{
    /// <summary>统一序列化配置：Web 默认约定 + 中文不转义（全局共用单例，线程安全）。</summary>
    public static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>序列化为 JSON 文本（统一配置）。</summary>
    /// <typeparam name="TValue">值类型。</typeparam>
    /// <param name="value">待序列化值（可为 null，输出 "null"）。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize<TValue>(TValue value) => JsonSerializer.Serialize(value, WebOptions);

    /// <summary>按运行时类型序列化为 JSON 文本（统一配置，适用于以 object 声明的多态值）。</summary>
    /// <param name="value">待序列化值。</param>
    /// <param name="inputType">值的具体类型。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(object? value, Type inputType) => JsonSerializer.Serialize(value, inputType, WebOptions);

    /// <summary>反序列化 JSON 文本（统一配置，属性名匹配忽略大小写）。</summary>
    /// <typeparam name="TValue">目标类型。</typeparam>
    /// <param name="json">JSON 文本。</param>
    /// <returns>反序列化结果。</returns>
    public static TValue? Deserialize<TValue>(string json) => JsonSerializer.Deserialize<TValue>(json, WebOptions);

    /// <summary>按指定类型反序列化 JSON 文本（统一配置）。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="returnType">目标类型。</param>
    /// <returns>反序列化结果。</returns>
    public static object? Deserialize(string json, Type returnType) => JsonSerializer.Deserialize(json, returnType, WebOptions);

    /// <summary>容错反序列化：空输入或非法 JSON 返回 false 且不抛异常（防御性解析不可信数据）。</summary>
    /// <typeparam name="TValue">目标类型。</typeparam>
    /// <param name="json">JSON 文本（可为 null / 空）。</param>
    /// <param name="value">反序列化结果（失败时为 default）。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryDeserialize<TValue>(string? json, out TValue? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            value = Deserialize<TValue>(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
