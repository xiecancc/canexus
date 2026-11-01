
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Domain.Tests.Exceptions;
/// <summary>BusinessException 业务异常单元测试。</summary>
[TestFixture]
public class BusinessExceptionTests
{
    [Test]
    public void Constructor_WithMessageOnly_UsesCommonInvalidRequestCode()
    {
        // 仅消息构造：默认使用通用请求错误码 40000
        var exception = new BusinessException("非法请求");

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo(ErrorCodes.CommonInvalidRequest));
            Assert.That(exception.Message, Is.EqualTo("非法请求"));
        });
    }

    [Test]
    public void Constructor_WithCodeAndMessage_CarriesBoth()
    {
        // 错误码 + 消息构造：两者均被携带
        var exception = new BusinessException(ErrorCodes.CommonDataDuplicate, "用户名已存在");

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo(40003));
            Assert.That(exception.Message, Is.EqualTo("用户名已存在"));
        });
    }

    [Test]
    public void Constructor_WithInnerException_CarriesInner()
    {
        // 带内部异常构造：保留异常链
        var inner = new InvalidOperationException("底层错误");
        var exception = new BusinessException(ErrorCodes.CommonOperationFailed, "操作失败", inner);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Code, Is.EqualTo(ErrorCodes.CommonOperationFailed));
            Assert.That(exception.Message, Is.EqualTo("操作失败"));
            Assert.That(exception.InnerException, Is.SameAs(inner));
        });
    }

    [Test]
    public void BusinessException_IsException()
    {
        // 业务异常属于 Exception 体系，可被全局异常中间件按类型捕获
        var exception = new BusinessException("x");

        Assert.That(exception, Is.InstanceOf<Exception>());
    }
}
