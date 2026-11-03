
using Domain.Shared.Results;

namespace Domain.Tests.Results;
/// <summary>Result 统一响应工厂方法单元测试。</summary>
[TestFixture]
public class ResultTests
{
    /// <summary>验证无数据的成功响应返回成功码 200 与默认提示消息。</summary>
    [Test]
    public void Ok_NoData_ReturnsSuccessCodeAndDefaultMessage()
    {
        // 无数据成功响应：成功码固定 200，消息为默认提示
        var result = Result.Ok();

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(Result.SuccessCode));
            Assert.That(result.Code, Is.EqualTo(200));
            Assert.That(result.Message, Is.EqualTo("操作成功"));
            Assert.That(result.IsSuccess, Is.True);
        });
    }

    /// <summary>验证带数据的成功响应返回泛型结果且数据载荷原样携带。</summary>
    [Test]
    public void Ok_WithData_ReturnsGenericResultWithData()
    {
        // 带数据成功响应：数据载荷原样返回
        var data = new { Name = "super" };
        var result = Result.Ok(data, "创建成功");

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(200));
            Assert.That(result.Message, Is.EqualTo("创建成功"));
            Assert.That(result.Data, Is.SameAs(data));
        });
    }

    /// <summary>验证未显式指定消息时成功响应使用默认消息。</summary>
    [Test]
    public void Ok_WithDefaultMessage_UsesDefaultMessage()
    {
        // 未显式指定消息时使用默认提示
        var result = Result.Ok(42);

        Assert.That(result.Message, Is.EqualTo("操作成功"));
    }

    /// <summary>验证失败响应按入参返回错误码与消息，且标记为非成功。</summary>
    [Test]
    public void Fail_ReturnsErrorCodeAndMessage()
    {
        // 失败响应：错误码与消息按入参返回
        var result = Result.Fail(ErrorCodes.CommonDataNotFound, "数据不存在");

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(40002));
            Assert.That(result.Message, Is.EqualTo("数据不存在"));
            Assert.That(result.IsSuccess, Is.False);
        });
    }

    /// <summary>验证失败响应支持携带任意自定义业务错误码。</summary>
    [Test]
    public void Fail_WithCustomCode_ReturnsCustomCode()
    {
        // 失败响应支持任意业务错误码
        var result = Result.Fail(400201, "用户不存在");

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(400201));
            Assert.That(result.IsSuccess, Is.False);
        });
    }

    /// <summary>验证成功码常量约定为 200。</summary>
    [Test]
    public void SuccessCode_IsTwoHundred()
    {
        // 成功码常量约定为 200
        Assert.That(Result.SuccessCode, Is.EqualTo(200));
    }
}
