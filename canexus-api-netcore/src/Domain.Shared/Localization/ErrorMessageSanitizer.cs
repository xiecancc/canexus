using System.Text.RegularExpressions;

namespace Domain.Shared.Localization;

/// <summary>
/// 错误消息脱敏器：在把「调用方原始文案」返回客户端之前，移除可能泄漏内部实现的高风险片段。
/// <para>背景（B3）：默认文化 zh 下 <see cref="ErrorMessageLocalizer"/> 直接回吐 <c>exception.Message</c>，
/// 而部分业务异常为便于排查会拼接动态诊断细节。一旦细节里含 SQL 片段、绝对文件路径、
/// 连接串口令、JWT / 令牌或堆栈帧，就会原样暴露给客户端。本脱敏器作为统一响应文案的**最后一道出口**
/// 施加于所有兜底文案（zh 直接返回路径、以及未收录错误码的回退路径），不改变静态资源表中的可信文案。</para>
/// <para>设计取舍：保留合法的动态细节（如「不支持的文件类型：'.exe'」、允许的扩展名列表），
/// 只针对明确的敏感模式做替换并限长；替换结果必须**幂等**（重复调用不再变化），
/// 因为异常消息可能先后经过本地化器与统一响应写入器。</para>
/// </summary>
public static partial class ErrorMessageSanitizer
{
    /// <summary>返回客户端的错误文案最大长度（超出部分截断并附省略号，防止长堆栈/长 SQL 外泄）。</summary>
    private const int MaxLength = 300;

    /// <summary>敏感片段的统一替换占位符（不含 ASCII 字母数字，保证二次脱敏不会再次命中令牌规则）。</summary>
    private const string Redacted = "[已隐藏]";

    /// <summary>
    /// 对错误文案做脱敏与限长。null / 空白返回空串；正常情况下合法中文文案原样保留。
    /// </summary>
    /// <param name="message">调用方原始文案（可能含动态诊断细节）。</param>
    /// <returns>可安全返回客户端的文案。</returns>
    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var text = message;
        text = StackFrameRegex().Replace(text, string.Empty);
        text = TokenRegex().Replace(text, Redacted);
        text = SecretAssignmentRegex().Replace(text, match => $"{match.Groups[1].Value}={Redacted}");
        text = SqlStatementRegex().Replace(text, Redacted);
        text = AbsolutePathRegex().Replace(text, Redacted);
        text = LongTokenRegex().Replace(text, Redacted);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return text.Length <= MaxLength
            ? text
            : string.Concat(text.AsSpan(0, MaxLength), "…");
    }

    /// <summary>堆栈帧（"at X in file:line N"）与异常链分隔行，可能暴露服务器内部路径与类型。</summary>
    [GeneratedRegex(@"\bat\s+[^\r\n]{0,200}?:line\s+\d+|---\s*>\s*[^\r\n]{0,200}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex StackFrameRegex();

    /// <summary>Bearer 令牌与三段式 JWT。</summary>
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._\-]{12,}|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex TokenRegex();

    /// <summary>连接串 / 配置中的口令与密钥赋值：保留键名以便排查，只隐藏值。</summary>
    [GeneratedRegex(@"\b(password|pwd|passwd|user\s*id|uid|api[_-]?key|secret|access[_-]?token|refresh[_-]?token)\s*=\s*[^;,\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SecretAssignmentRegex();

    /// <summary>SQL 语句片段（单行、至分号或换行结束），防止表结构 / 查询条件外泄。</summary>
    [GeneratedRegex(@"\b(?:SELECT\s|INSERT\s+INTO\s|UPDATE\s|DELETE\s+FROM\s|DROP\s+TABLE\s|ALTER\s+TABLE\s)[^;\r\n]{0,200}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SqlStatementRegex();

    /// <summary>绝对文件路径（Windows 盘符或常见 Unix 系统目录），刻意不匹配 /api/... 这类 URL。</summary>
    [GeneratedRegex(@"(?:[A-Za-z]:\\|/(?:usr|var|home|etc|tmp|opt|app|www|root|mnt)/)[^\s""'<>|]{0,200}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AbsolutePathRegex();

    /// <summary>无语义的长随机串（base64 / hex 形态的令牌、密钥），阈值 48 避免误伤普通标识。</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9+/=_\-]{48,}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LongTokenRegex();

    /// <summary>把换行与连续空白归一为单空格，避免多行堆栈 / 长文本原样进入响应体。</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();
}
