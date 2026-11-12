using Domain.Shared.Localization;

namespace Domain.Tests.Localization;

/// <summary>
/// 错误消息脱敏器单元测试（B3）：验证高风险片段被移除、合法动态细节保留、脱敏幂等。
/// </summary>
[TestFixture]
public sealed class ErrorMessageSanitizerTests
{
    /// <summary>null / 空白输入返回空串。</summary>
    [Test]
    public void NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ErrorMessageSanitizer.Sanitize(null), Is.Empty);
            Assert.That(ErrorMessageSanitizer.Sanitize("  "), Is.Empty);
        });
    }

    /// <summary>合法中文动态细节（文件类型提示）原样保留。</summary>
    [Test]
    public void NormalChineseMessage_Preserved()
    {
        const string message = "不支持的文件类型：'.exe'（允许的扩展名：.jpg、.png）";

        Assert.That(ErrorMessageSanitizer.Sanitize(message), Is.EqualTo(message));
    }

    /// <summary>Bearer 令牌被替换为占位符。</summary>
    [Test]
    public void BearerToken_Redacted()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("认证失败：Bearer abcdefghijklmnopqrst");

        Assert.That(sanitized, Does.Contain("[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain("abcdefghijklmnopqrst"));
    }

    /// <summary>三段式 JWT 被替换为占位符。</summary>
    [Test]
    public void JwtTripleToken_Redacted()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize(
            "解析令牌失败：eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6y");

        Assert.That(sanitized, Does.Contain("[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain("eyJhbGciOiJIUzI1NiJ9"));
    }

    /// <summary>口令赋值只隐藏值、保留键名。</summary>
    [Test]
    public void SecretAssignment_ValueHidden_KeyKept()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("连接失败：Server=localhost;Password=Sup3rS3cret!;Database=app");

        Assert.That(sanitized, Does.Contain("Password=[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain("Sup3rS3cret"));
    }

    /// <summary>SQL 语句片段被整体替换。</summary>
    [Test]
    public void SqlStatement_Redacted()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("执行失败：SELECT * FROM sys_user WHERE password = 'x'");

        Assert.That(sanitized, Does.Contain("[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain("sys_user"));
    }

    /// <summary>Windows 绝对路径被替换，URL 路径（/api/...）不受影响。</summary>
    [Test]
    public void AbsoluteWindowsPath_Redacted_UrlPathKept()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("读取配置失败：C:\\apps\\secret\\appsettings.json；接口 /api/v1/users 正常");

        Assert.That(sanitized, Does.Contain("[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain("appsettings.json"));
        Assert.That(sanitized, Does.Contain("/api/v1/users"));
    }

    /// <summary>堆栈帧「at X in file:line N」被移除。</summary>
    [Test]
    public void StackFrameLine_Removed()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("业务异常 at SuperAdmin.Application.Commands.UserHandlers in D:\\src\\UserHandlers.cs:line 88");

        Assert.That(sanitized, Does.Not.Contain("UserHandlers.cs"));
    }

    /// <summary>长随机令牌串（≥48 字符）被替换。</summary>
    [Test]
    public void LongRandomToken_Redacted()
    {
        var token = new string('a', 60) + "7f3b";
        var sanitized = ErrorMessageSanitizer.Sanitize($"刷新令牌无效：{token}");

        Assert.That(sanitized, Does.Contain("[已隐藏]"));
        Assert.That(sanitized, Does.Not.Contain(token));
    }

    /// <summary>超长文案按 300 字符截断并附省略号。</summary>
    [Test]
    public void OverlongMessage_Truncated()
    {
        var message = new string('好', 600);
        var sanitized = ErrorMessageSanitizer.Sanitize(message);

        Assert.That(sanitized.Length, Is.LessThanOrEqualTo(301));
        Assert.That(sanitized, Does.EndWith("…"));
    }

    /// <summary>多行文本归一为单行（空白折叠）。</summary>
    [Test]
    public void MultilineText_NormalizedToSingleLine()
    {
        var sanitized = ErrorMessageSanitizer.Sanitize("第一行\r\n第二行\t第三行");

        Assert.That(sanitized, Is.EqualTo("第一行 第二行 第三行"));
    }

    /// <summary>脱敏幂等：对结果再次脱敏结果不变。</summary>
    [Test]
    public void Sanitize_IsIdempotent()
    {
        const string message = "认证失败：Bearer abcdefghijklmnopqrst，配置 D:\\secret\\appsettings.json，Password=TopSecret123";

        var first = ErrorMessageSanitizer.Sanitize(message);
        var second = ErrorMessageSanitizer.Sanitize(first);

        Assert.That(second, Is.EqualTo(first));
    }
}
