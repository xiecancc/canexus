using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Domain.Shared.Jobs;

/// <summary>
/// 定时任务参数模型：<c>SysJob.ParametersJson</c> 的唯一解析口径。
/// <para>作业参数限定为「JSON 对象 + 字符串/数字/布尔/null 标量值」，写入 Quartz <c>JobDataMap</c> 时
/// 统一转为字符串，避免自定义类型经 Quartz 序列化器（json / useProperties=false）往返后类型漂移。
/// 拒绝数组与嵌套对象：调度参数是运维可编辑的扁平键值表，嵌套结构只会增加误配与反序列化风险。</para>
/// <para>本类型不依赖 Quartz：Quartz 上下文的读取与 JobDataMap 装配留在 Infrastructure 调度层，
/// 应用层验证器与领域模型可直接引用，不产生反向依赖。</para>
/// </summary>
public static class JobParameters
{
    /// <summary>参数签名在 JobDataMap 中的键名：用于调度差异比对（参数变化即替换 JobDetail）。</summary>
    public const string SignatureKey = "jobParametersSignature";

    /// <summary>调度包装任务占用的 JobDataMap 键名；作业参数不得覆盖，否则任务无法定位自身定义。</summary>
    public const string JobIdKey = "jobId";

    /// <summary>ParametersJson 最大长度（与实体列宽一致）。</summary>
    public const int MaxJsonLength = 2000;

    /// <summary>参数键数量上限。</summary>
    public const int MaxKeyCount = 32;

    /// <summary>单个参数键最大长度。</summary>
    public const int MaxKeyLength = 64;

    /// <summary>单个参数值最大长度。</summary>
    public const int MaxValueLength = 512;

    /// <summary>保留键（不区分大小写）：作业参数不得占用调度框架自行写入的键。</summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        JobIdKey,
        SignatureKey
    };

    /// <summary>
    /// 校验参数 JSON 是否合法；合法返回 null，非法返回可直接展示给调用方的错误说明。
    /// <para>空 / 空白视为「无参数」，合法。</para>
    /// </summary>
    /// <param name="json">参数 JSON 文本。</param>
    /// <returns>合法返回 null；否则返回错误说明。</returns>
    public static string? Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (json.Length > MaxJsonLength)
        {
            return $"作业参数长度不能超过 {MaxJsonLength} 个字符";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return "作业参数必须是合法的 JSON 对象";
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "作业参数必须是 JSON 对象（如 {\"RetentionDays\":30}）";
            }

            var count = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                count++;
                if (count > MaxKeyCount)
                {
                    return $"作业参数最多允许 {MaxKeyCount} 个键";
                }

                if (string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > MaxKeyLength)
                {
                    return $"参数键不能为空且长度不能超过 {MaxKeyLength} 个字符";
                }

                if (ReservedKeys.Contains(property.Name))
                {
                    return $"参数键「{property.Name}」为调度框架保留键，不可使用";
                }

                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    return $"参数「{property.Name}」的值只能是字符串、数字、布尔或 null，不支持数组与嵌套对象";
                }

                var value = ScalarToString(property.Value);
                if (value.Length > MaxValueLength)
                {
                    return $"参数「{property.Name}」的值长度不能超过 {MaxValueLength} 个字符";
                }
            }
        }

        return null;
    }

    /// <summary>判定参数 JSON 是否合法（验证器用的布尔便捷入口）。</summary>
    /// <param name="json">参数 JSON 文本。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsValid(string? json) => Validate(json) is null;

    /// <summary>
    /// 解析参数 JSON 为扁平键值表（值统一转字符串，null 转空串）。
    /// </summary>
    /// <param name="json">参数 JSON 文本；空表示无参数。</param>
    /// <returns>按键名排序后的只读键值表。</returns>
    /// <exception cref="InvalidOperationException">参数 JSON 不合法时抛出（入调度前的 fail-fast 兜底）。</exception>
    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        var problem = Validate(json);
        if (problem is not null)
        {
            throw new InvalidOperationException($"作业参数不合法：{problem}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(json);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = ScalarToString(property.Value);
        }

        return result;
    }

    /// <summary>生成参数签名：按键名排序后拼接，保证同一组参数在任意写入顺序下签名一致。</summary>
    /// <param name="parameters">已解析的参数键值表。</param>
    /// <returns>规范化的签名文本。</returns>
    public static string Signature(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 读取整型参数：缺失 / 非数字 / 越界时回退到默认值（作业参数由运维编辑，容错优先于抛异常，
    /// 实际取值与越界回退均由作业日志记录）。
    /// </summary>
    /// <param name="parameters">参数键值表。</param>
    /// <param name="key">参数键名。</param>
    /// <param name="defaultValue">默认值。</param>
    /// <param name="min">允许的最小值（含）。</param>
    /// <param name="max">允许的最大值（含）。</param>
    /// <returns>落在 [min, max] 内的整数；无法解析或越界时返回默认值。</returns>
    public static int GetInt(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        int defaultValue,
        int min,
        int max)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(key, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < min
            || value > max)
        {
            return defaultValue;
        }

        return value;
    }

    /// <summary>把 JSON 标量转成字符串（null 转空串，布尔转 true/false，数字保留原始文本）。</summary>
    /// <param name="element">JSON 标量元素。</param>
    /// <returns>字符串表示。</returns>
    private static string ScalarToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        _ => element.GetRawText()
    };
}
