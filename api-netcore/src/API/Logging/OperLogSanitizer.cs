
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace API.Logging;
/// <summary>
/// 操作日志数据清洗器（纯逻辑，供过滤器与单元测试复用）：
/// <list type="bullet">
/// <item>敏感字段脱敏：password / oldPassword / newPassword / confirmPassword / captchaCode 等统一替换为 ******；</item>
/// <item>超长文本截断（请求参数与错误消息统一 2000 字符上限）。</item>
/// </list>
/// </summary>
public static class OperLogSanitizer
{
    /// <summary>脱敏占位文本。</summary>
    public const string MaskText = "******";

    /// <summary>默认截断长度（请求参数 / 错误消息统一上限）。</summary>
    public const int MaxTextLength = 2000;

    /// <summary>需要脱敏的字段名集合（比较忽略大小写）。</summary>
    public static readonly IReadOnlySet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "oldPassword",
        "newPassword",
        "confirmPassword",
        "captchaCode"
    };

    // 不转义非 ASCII 字符：日志中的中文参数保持可读
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>对 JSON 文本中的敏感字段脱敏（递归处理嵌套对象与数组；解析失败原样返回）。</summary>
    /// <param name="json">原始 JSON 文本。</param>
    /// <returns>脱敏后的 JSON 文本。</returns>
    public static string MaskSensitiveFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return json;
            }

            MaskNode(node);
            return node.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            // 序列化产物理应合法，防御性兜底：不因脱敏失败阻断日志写入
            return json;
        }
    }

    /// <summary>按长度上限截断文本（空值返回空字符串）。</summary>
    /// <param name="value">原始文本。</param>
    /// <param name="maxLength">长度上限，默认 2000。</param>
    /// <returns>截断后的文本。</returns>
    public static string Truncate(string? value, int maxLength = MaxTextLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>递归脱敏 JSON 节点：命中敏感字段名即替换值，否则继续下钻对象与数组。</summary>
    private static void MaskNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (SensitiveFields.Contains(property.Key))
                {
                    obj[property.Key] = MaskText;
                }
                else if (property.Value is not null)
                {
                    MaskNode(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    MaskNode(item);
                }
            }
        }
    }
}
