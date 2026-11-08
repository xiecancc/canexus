
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Domain.Shared.Json;

namespace Domain.Shared.Logging;

/// <summary>
/// 敏感数据脱敏器（纯逻辑共享内核）：操作日志 JSON 载荷与 SQL 参数日志共用一套敏感字段规则，
/// 避免两处各自维护导致口径漂移。
/// <list type="bullet">
/// <item>字段名判定（JSON 属性 / SQL 参数 / 实体属性 / QueryString 参数）统一走
/// <see cref="IsSensitiveField"/>：按关键字包含匹配，覆盖 PasswordHash、userPwd、access_token、
/// idCard、idNo、phone、mobile、telephone 等变体，命中的值一律替换为 <see cref="MaskText"/>；</item>
/// <item>JSON 载荷脱敏：递归处理嵌套对象与数组；</item>
/// <item>自由文本脱敏：URL QueryString 与异常消息中的 key=value / JSON 片段 / Bearer 令牌经
/// <see cref="MaskQueryString"/> 与 <see cref="MaskFreeText"/> 清洗，避免明文落到 sys_oper_log。</item>
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

    /// <summary>敏感关键字（包含匹配，比较忽略大小写）：覆盖密码哈希、令牌、验证码、密钥、
    /// 证件号与手机号等字段变体（如 PasswordHash / user_pwd / access_token / idCard / mobile）。</summary>
    private static readonly string[] SensitiveKeywords =
    [
        "password",
        "pwd",
        "token",
        "secret",
        "captcha",
        "credential",
        "idcard",
        "idno",
        "certno",
        "identity",
        "phone",
        "mobile",
        "telephone"
    ];

    /// <summary>异常消息中的 JSON 字符串赋值（敏感判定统一交给 <see cref="IsSensitiveField"/>）。</summary>
    private static readonly Regex JsonAssignmentPattern = new(
        "\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    /// <summary>异常消息 / QueryString 中的 key=value 赋值（含引号值）。</summary>
    private static readonly Regex KeyValuePattern = new(
        "(?<key>[A-Za-z0-9_@.\\-]+)\\s*=\\s*(?<value>\"[^\"]*\"|'[^']*'|[^\\s&,;]+)",
        RegexOptions.Compiled);

    /// <summary>Bearer 访问令牌明文。</summary>
    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[A-Za-z0-9._\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>
    /// 统一敏感字段名判定：JSON 属性、SQL 参数、实体属性、QueryString 参数共用一套关键字规则
    /// （包含匹配，忽略大小写与参数前缀 @），避免各落点口径漂移。
    /// </summary>
    /// <param name="fieldName">字段名（如 @PasswordHash / user_pwd / idCardNo）。</param>
    /// <returns>true 表示该字段的值应替换为 <see cref="MaskText"/>。</returns>
    public static bool IsSensitiveField(string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return false;
        }

        // 归一化：去掉下划线 / 连字符 / 点 / @ 等分隔符后再匹配，覆盖
        // id_card_no、user-pwd、@Password.Hash 等命名变体
        var normalized = new string([.. fieldName.Where(char.IsLetterOrDigit)]);
        return SensitiveKeywords.Any(keyword =>
            normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>判别 SQL 参数名是否为敏感参数（委托统一字段判定）。</summary>
    /// <param name="parameterName">SQL 参数名（如 @PasswordHash）。</param>
    /// <returns>true 表示敏感参数，日志中应替换为 <see cref="MaskText"/>。</returns>
    public static bool IsSensitiveParameter(string? parameterName) => IsSensitiveField(parameterName);

    /// <summary>
    /// 脱敏 URL 中的 QueryString：仅替换敏感参数的值，路径与其它参数原样保留（可传入完整 URL）。
    /// </summary>
    /// <param name="url">完整 URL 或查询串。</param>
    /// <returns>脱敏后的文本。</returns>
    public static string MaskQueryString(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url ?? string.Empty;
        }

        var separator = url.IndexOf('?');
        if (separator < 0 || separator == url.Length - 1)
        {
            return url;
        }

        var prefix = url[..(separator + 1)];
        var parts = url[(separator + 1)..].Split('&');
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var equals = part.IndexOf('=');
            var name = equals < 0 ? part : part[..equals];
            if (IsSensitiveField(name))
            {
                parts[index] = $"{name}={MaskText}";
            }
        }

        return prefix + string.Join("&", parts);
    }

    /// <summary>脱敏自由文本（异常消息、SQL 日志片段）：JSON 赋值、key=value 与 Bearer 令牌统一替换。</summary>
    /// <param name="text">原始文本。</param>
    /// <returns>脱敏后的文本。</returns>
    public static string MaskFreeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // 正则只负责定位赋值结构，是否脱敏统一由字段名判定（与 JSON / SQL / QueryString 同口径）
        var masked = JsonAssignmentPattern.Replace(text, match =>
            IsSensitiveField(match.Groups["key"].Value)
                ? $"\"{match.Groups["key"].Value}\":\"{MaskText}\""
                : match.Value);
        masked = KeyValuePattern.Replace(masked, match =>
            IsSensitiveField(match.Groups["key"].Value)
                ? $"{match.Groups["key"].Value}={MaskText}"
                : match.Value);
        return BearerPattern.Replace(masked, $"Bearer {MaskText}");
    }

    /// <summary>递归脱敏 JSON 节点：命中敏感字段名即替换值，否则继续下钻对象与数组。</summary>
    /// <param name="node">当前处理的 JSON 节点。</param>
    private static void MaskNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (SensitiveFields.Contains(property.Key) || IsSensitiveField(property.Key))
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
