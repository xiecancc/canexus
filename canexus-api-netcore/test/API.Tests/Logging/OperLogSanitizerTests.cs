
using API.Logging;
using NUnit.Framework;

namespace API.Tests.Logging;
/// <summary>操作日志数据清洗器单元测试（B8，纯逻辑）。</summary>
[TestFixture]
public class OperLogSanitizerTests
{
    [Test]
    public void MaskSensitiveFields_MasksTopLevelSensitiveProperty()
    {
        var masked = OperLogSanitizer.MaskSensitiveFields("""{"password":"Super@Secret","userName":"admin"}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Contain("******"));
            Assert.That(masked, Does.Not.Contain("Super@Secret"), "密码明文不得出现在日志中");
            Assert.That(masked, Does.Contain("admin"), "非敏感字段应保留");
        });
    }

    [Test]
    public void MaskSensitiveFields_MasksNestedAndArrayItems()
    {
        var masked = OperLogSanitizer.MaskSensitiveFields(
            """{"user":{"newPassword":"abc123","nickName":"张三"},"list":[{"captchaCode":"x9yk"}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Not.Contain("abc123"));
            Assert.That(masked, Does.Not.Contain("x9yk"));
            Assert.That(masked, Does.Contain("张三"));
        });
    }

    [Test]
    public void MaskSensitiveFields_FieldNameCaseInsensitive()
    {
        var masked = OperLogSanitizer.MaskSensitiveFields("""{"PASSWORD":"p@ss","OldPassword":"old@ss"}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Not.Contain("p@ss"));
            Assert.That(masked, Does.Not.Contain("old@ss"));
        });
    }

    [Test]
    public void MaskSensitiveFields_InvalidJson_ReturnsOriginal()
    {
        const string raw = "不是 JSON 文本";

        Assert.That(OperLogSanitizer.MaskSensitiveFields(raw), Is.EqualTo(raw), "解析失败应原样返回，不阻断日志写入");
    }

    [Test]
    public void Truncate_LongText_CutToLimit()
    {
        var result = OperLogSanitizer.Truncate(new string('a', 2500));

        Assert.Multiple(() =>
        {
            Assert.That(result.Length, Is.EqualTo(2000), "默认上限 2000 字符");
            Assert.That(result, Is.All.EqualTo('a'));
        });
    }

    [Test]
    public void Truncate_ShortOrEmpty_ReturnsAsIsOrEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperLogSanitizer.Truncate("短文本"), Is.EqualTo("短文本"));
            Assert.That(OperLogSanitizer.Truncate(null), Is.Empty, "空值应返回空字符串");
            Assert.That(OperLogSanitizer.Truncate("abc", 2), Is.EqualTo("ab"), "自定义上限生效");
        });
    }
}
