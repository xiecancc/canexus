
using System.Reflection;
using Domain.Shared.Results;

namespace Domain.Tests.Results;
/// <summary>错误码分段与 HTTP 状态码映射契约测试（T-16：清理 404 死分支）。</summary>
[TestFixture]
public class ErrorCodesTests
{
    /// <summary>认证 / 权限 / 系统段与特例映射保持稳定。</summary>
    [Test]
    public void GetHttpStatusCode_KeepsActiveSegmentsAndSpecialCases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ErrorCodes.GetHttpStatusCode(Result.SuccessCode), Is.EqualTo(200));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthCaptchaError), Is.EqualTo(400),
                "验证码错误须保持 400，避免触发前端刷新令牌重试");
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthAccountOrPasswordError), Is.EqualTo(400),
                "账号或密码错误属登录表单语义（C3），返回 401 会被前端吞掉文案并误触发刷新链路");
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthAccountLocked), Is.EqualTo(400),
                "账号锁定属登录表单语义（C3），返回 401 会被前端吞掉文案并误触发刷新链路");
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthUnauthorized), Is.EqualTo(401));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthTokenExpired), Is.EqualTo(401));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthRefreshTokenInvalid), Is.EqualTo(401));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthTokenRevoked), Is.EqualTo(401));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.AuthForbidden), Is.EqualTo(403));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.CommonSystemError), Is.EqualTo(500));
            Assert.That(ErrorCodes.GetHttpStatusCode(ErrorCodes.CommonConcurrencyConflict), Is.EqualTo(400));
        });
    }

    /// <summary>404xx 段已无常量，该区间不再被映射为 404（死分支不得复活）。</summary>
    [Test]
    public void GetHttpStatusCode_FormerDead404Branch_NowFallsBackTo400()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ErrorCodes.GetHttpStatusCode(40400), Is.EqualTo(400));
            Assert.That(ErrorCodes.GetHttpStatusCode(40499), Is.EqualTo(400));
        });
    }

    /// <summary>全量常量中不存在 404xx 段定义，防止注释与实现再次漂移。</summary>
    [Test]
    public void ErrorCodeConstants_ContainNo404Segment()
    {
        var codes = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(int))
            .Select(field => (int)field.GetValue(null)!)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(codes, Is.Not.Empty);
            Assert.That(codes.Where(code => code is >= 40400 and <= 40499), Is.Empty);
        });
    }
}
