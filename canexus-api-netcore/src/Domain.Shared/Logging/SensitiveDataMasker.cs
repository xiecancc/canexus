
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.Shared.Json;

namespace Domain.Shared.Logging;

/// <summary>
/// 敏感数据脱敏器（纯逻辑共享内核）：操作日志 JSON 载荷与 SQL 参数日志共用一套敏感字段规则，
/// 避免两处各自维护导致口径漂移。
/// <list type="bullet">
/// <item>JSON 载荷脱敏：敏感字段名（password / oldPassword / newPassword / confirmPassword / captchaCode 等）
/// 统一替换为 <see cref="MaskText"/>，递归处理嵌套对象与数组；</item>
/// <item>SQL 参数名判别：按关键字包含匹配（password / pwd / token / secret / captcha），
/// 命中的参数值在 SQL 日志中不落明文。</item>
/// </list>
/// </summary>
public static class SensitiveDataMasker
{
    /// <summary>脱敏占位文本。</summary>
    public const string MaskText = "******";

    /// <summary>JSON 载荷需要脱敏的字段名集合（精确匹配，比较忽略大小写）。</summary>
    public static readonly IReadOnlySet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "oldPassword",
        "newPassword",
        "confirmPassword",
        "captchaCode",
        "refreshToken",
        "accessToken"
    };

    /// <summary>SQL 参数名敏感关键字（包含匹配，比较忽略大小写）：
    /// 覆盖密码哈希、令牌哈希、验证码与密钥类列名（如 PasswordHash / RefreshTokenHash / Secret）。</summary>
    private static readonly string[] SensitiveKeywords =
    [
        "password",
        "pwd",
        "token",
        "secret",
        "captcha"
    ];

    /// <summary>对 JSON 文本中的敏感字段脱敏（递归处理嵌套对象与数组；解析失败原样返回，不阻断日志写入）。</summary>
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
            return node.ToJsonString(JsonHelper.WebOptions);
        }
        catch (JsonException)
        {
            // 序列化产物理应合法，防御性兜底：不因脱敏失败阻断日志写入
            return json;
        }
    }

    /// <summary>判别 SQL 参数名是否为敏感参数（按关键字包含匹配，忽略大小写与参数前缀 @）。</summary>
    /// <param name="parameterName">SQL 参数名（如 @PasswordHash）。</param>
    /// <returns>true 表示敏感参数，日志中应替换为 <see cref="MaskText"/>。</returns>
    public static bool IsSensitiveParameter(string? parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
        {
            return false;
        }

        return SensitiveKeywords.Any(keyword =>
            parameterName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>递归脱敏 JSON 节点：命中敏感字段名即替换值，否则继续下钻对象与数组。</summary>
    /// <param name="node">当前处理的 JSON 节点。</param>
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
