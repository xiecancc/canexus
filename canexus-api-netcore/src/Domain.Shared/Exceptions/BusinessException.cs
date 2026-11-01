namespace Domain.Shared.Exceptions;

/// <summary>
/// 业务异常：携带业务错误码与用户可读消息。
/// <para>由全局异常处理中间件统一捕获，并按错误码映射为 HTTP 状态后返回统一响应结构 <see cref="Results.Result"/>。</para>
/// </summary>
public class BusinessException : Exception
{
    /// <summary>业务错误码（分段规则见 <see cref="Results.ErrorCodes"/>）。</summary>
    public int Code { get; }

    /// <summary>仅以消息构造（使用通用请求错误码 <see cref="Results.ErrorCodes.CommonInvalidRequest"/>）。</summary>
    /// <param name="message">错误描述。</param>
    public BusinessException(string message)
        : this(Results.ErrorCodes.CommonInvalidRequest, message)
    {
    }

    /// <summary>以错误码与消息构造。</summary>
    /// <param name="code">业务错误码。</param>
    /// <param name="message">错误描述。</param>
    public BusinessException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>以错误码、消息与内部异常构造。</summary>
    /// <param name="code">业务错误码。</param>
    /// <param name="message">错误描述。</param>
    /// <param name="innerException">内部异常。</param>
    public BusinessException(int code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
