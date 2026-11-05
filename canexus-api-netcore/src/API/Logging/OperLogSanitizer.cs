
using Domain.Shared.Logging;

namespace API.Logging;
/// <summary>
/// 操作日志数据清洗器（纯逻辑，供过滤器与单元测试复用）：
/// <list type="bullet">
/// <item>敏感字段脱敏：password / oldPassword / newPassword / confirmPassword / captchaCode 等统一替换为 ******；</item>
/// <item>超长文本截断（请求参数与错误消息统一 2000 字符上限）。</item>
/// </list>
/// 脱敏规则下沉于共享内核 <see cref="SensitiveDataMasker"/>（SQL 参数日志共用同一套规则），本类仅保留截断与委托入口。
/// </summary>
public static class OperLogSanitizer
{
    /// <summary>脱敏占位文本（与共享内核一致）。</summary>
    public const string MaskText = Domain.Shared.Logging.SensitiveDataMasker.MaskText;

    /// <summary>默认截断长度（请求参数 / 错误消息统一上限）。</summary>
    public const int MaxTextLength = 2000;

    /// <summary>对 JSON 文本中的敏感字段脱敏（递归处理嵌套对象与数组；解析失败原样返回）。</summary>
    /// <param name="json">原始 JSON 文本。</param>
    /// <returns>脱敏后的 JSON 文本。</returns>
    public static string MaskSensitiveFields(string json)
    {
        return Domain.Shared.Logging.SensitiveDataMasker.MaskSensitiveFields(json);
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
}
