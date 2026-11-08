
using Domain.Shared.Logging;

namespace Domain.Tests.Logging;
/// <summary>敏感数据脱敏器单元测试（共享内核规则：操作日志 JSON 载荷与 SQL 参数日志共用）。</summary>
[TestFixture]
public class SensitiveDataMaskerTests
{
    /// <summary>验证 JSON 载荷脱敏会掩码顶层敏感属性并保留非敏感字段。</summary>
    [Test]
    public void MaskSensitiveFields_MasksTopLevelSensitiveProperty()
    {
        var masked = SensitiveDataMasker.MaskSensitiveFields("""{"password":"Super@Secret","userName":"admin"}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Contain(SensitiveDataMasker.MaskText));
            Assert.That(masked, Does.Not.Contain("Super@Secret"), "密码明文不得出现在日志中");
            Assert.That(masked, Does.Contain("admin"), "非敏感字段应保留");
        });
    }

    /// <summary>验证 JSON 载荷脱敏会掩码嵌套对象与数组元素内的敏感属性，且字段名大小写不敏感。</summary>
    [Test]
    public void MaskSensitiveFields_MasksNestedAndCaseInsensitive()
    {
        var masked = SensitiveDataMasker.MaskSensitiveFields(
            """{"user":{"PASSWORD":"abc123","nickName":"张三"},"list":[{"captchaCode":"x9yk"}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Not.Contain("abc123"));
            Assert.That(masked, Does.Not.Contain("x9yk"));
            Assert.That(masked, Does.Contain("张三"));
        });
    }

    /// <summary>验证非法 JSON 文本脱敏时原样返回，不阻断日志写入。</summary>
    [Test]
    public void MaskSensitiveFields_InvalidJson_ReturnsOriginal()
    {
        const string raw = "不是 JSON 文本";

        Assert.That(SensitiveDataMasker.MaskSensitiveFields(raw), Is.EqualTo(raw), "解析失败应原样返回，不阻断日志写入");
    }

    /// <summary>验证 SQL 参数名关键字判别：密码 / 令牌 / 验证码 / 密钥类参数命中（忽略大小写与 @ 前缀）。</summary>
    [Test]
    public void IsSensitiveParameter_KeywordMatch_ReturnsTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@PasswordHash"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@RefreshTokenHash"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@CaptchaCode"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@pwd"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@ClientSecret"), Is.True);
        });
    }

    /// <summary>验证普通业务参数名不命中敏感判别。</summary>
    [Test]
    public void IsSensitiveParameter_OrdinaryName_ReturnsFalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@UserName"), Is.False);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter("@DeptId"), Is.False);
            Assert.That(SensitiveDataMasker.IsSensitiveParameter(null), Is.False, "空参数名安全返回 false");
            Assert.That(SensitiveDataMasker.IsSensitiveParameter(string.Empty), Is.False);
        });
    }

    /// <summary>T-15：JSON / SQL / 实体字段名共用一套规则，覆盖密码哈希、令牌、证件与手机号变体。</summary>
    [Test]
    public void MaskSensitiveFields_CoversSensitiveFieldVariants()
    {
        var masked = SensitiveDataMasker.MaskSensitiveFields(
            """{"PasswordHash":"$2a$hash","user_pwd":"p1","access_token":"tok1","idCardNo":"110101199001011234","mobile":"13800138000","telephone":"01088886666","nickName":"张三"}""");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Not.Contain("$2a$hash"));
            Assert.That(masked, Does.Not.Contain("p1\""), "user_pwd 变体须脱敏");
            Assert.That(masked, Does.Not.Contain("tok1"));
            Assert.That(masked, Does.Not.Contain("110101199001011234"));
            Assert.That(masked, Does.Not.Contain("13800138000"));
            Assert.That(masked, Does.Not.Contain("01088886666"));
            Assert.That(masked, Does.Contain("张三"), "非敏感字段保持原值");
        });
    }

    /// <summary>T-15：字段判定口径统一，证件 / 手机号变体命中，普通字段不误伤。</summary>
    [Test]
    public void IsSensitiveField_SharesRulesAcrossAllSinks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SensitiveDataMasker.IsSensitiveField("PasswordHash"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveField("id_card_no"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveField("IdNo"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveField("MobilePhone"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveField("@RefreshTokenHash"), Is.True);
            Assert.That(SensitiveDataMasker.IsSensitiveField("NickName"), Is.False);
            Assert.That(SensitiveDataMasker.IsSensitiveField("OrderNum"), Is.False);
        });
    }

    /// <summary>T-15：QueryString 中仅敏感参数值被替换，路径与其它参数保持原样。</summary>
    [Test]
    public void MaskQueryString_MasksOnlySensitiveValues()
    {
        var masked = SensitiveDataMasker.MaskQueryString(
            "/api/system/user/page?pageIndex=1&token=eyJhbGciOi&password=plainPwd&keyword=keepme");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.StartWith("/api/system/user/page?"));
            Assert.That(masked, Does.Not.Contain("eyJhbGciOi"));
            Assert.That(masked, Does.Not.Contain("plainPwd"));
            Assert.That(masked, Does.Contain("pageIndex=1"));
            Assert.That(masked, Does.Contain("keyword=keepme"));
            Assert.That(masked, Does.Contain(SensitiveDataMasker.MaskText));
        });
    }

    /// <summary>T-15：异常消息中的 JSON 赋值、SQL 参数赋值与 Bearer 令牌均脱敏。</summary>
    [Test]
    public void MaskFreeText_MasksAssignmentsAndBearerTokens()
    {
        const string message =
            "SQL failed: @PasswordHash='bcrypt$secret' ; payload={\"accessToken\":\"jwt.value\"} ; Authorization: Bearer eyJhbGciOiJIUzI1NiJ9";
        var masked = SensitiveDataMasker.MaskFreeText(message);

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Not.Contain("bcrypt$secret"));
            Assert.That(masked, Does.Not.Contain("jwt.value"));
            Assert.That(masked, Does.Not.Contain("eyJhbGciOiJIUzI1NiJ9"));
            Assert.That(masked, Does.Contain("SQL failed"), "非敏感上下文应保留");
        });
    }
}
