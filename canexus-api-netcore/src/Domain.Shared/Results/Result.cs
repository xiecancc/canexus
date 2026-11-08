using System.Text.Json.Serialization;

namespace Domain.Shared.Results;

/// <summary>
/// 统一响应结果（无数据载荷版本）。
/// <para>响应体为 <c>{ code, message }</c>：成功码 200，业务失败码见 <see cref="ErrorCodes"/> 分段规则；
/// 带载荷的成功响应由 <see cref="Result{T}"/> 追加 <c>data</c> 字段。</para>
/// <para>T-16 契约约定：成功工厂只保留 <see cref="Ok()"/>（无载荷 / 默认消息）、
/// <see cref="OkMessage(string)"/>（无载荷 / 自定义消息）与 <see cref="Ok{T}(T, string)"/>（带载荷）三种入口。
/// 不再提供 <c>Ok(string message)</c> 重载——它与 <c>Ok&lt;string&gt;(data)</c> 的单字符串实参形态无法区分，
/// 会把本应作为 data 的字符串静默改写成 message。</para>
/// </summary>
public class Result
{
    /// <summary>成功状态码。</summary>
    public const int SuccessCode = 200;

    /// <summary>响应状态码：200 表示成功，其余为业务/系统错误码（分段规则见 <see cref="ErrorCodes"/>）。</summary>
    public int Code { get; init; }

    /// <summary>响应消息：成功时为提示信息，失败时为错误描述。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>是否成功（仅供服务端内部判断，不参与 JSON 序列化，保证响应体结构一致）。</summary>
    [JsonIgnore]
    public bool IsSuccess => Code == SuccessCode;

    /// <summary>创建成功响应（无数据载荷，使用默认提示消息）。</summary>
    /// <returns>统一响应结果。</returns>
    public static Result Ok() => new() { Code = SuccessCode, Message = "操作成功" };

    /// <summary>创建成功响应（无数据载荷，自定义提示消息）。</summary>
    /// <param name="message">提示消息。</param>
    /// <returns>统一响应结果。</returns>
    public static Result OkMessage(string message) => new() { Code = SuccessCode, Message = message };

    /// <summary>创建成功响应（带数据载荷）。</summary>
    /// <typeparam name="T">数据载荷类型。</typeparam>
    /// <param name="data">响应数据（字符串同样是合法载荷，不会被当作提示消息）。</param>
    /// <param name="message">提示消息。</param>
    /// <returns>带数据的统一响应结果。</returns>
    public static Result<T> Ok<T>(T data, string message = "操作成功") => new() { Code = SuccessCode, Message = message, Data = data };

    /// <summary>创建失败响应。</summary>
    /// <param name="code">业务错误码（分段规则见 <see cref="ErrorCodes"/>）。</param>
    /// <param name="message">错误描述。</param>
    /// <returns>统一响应结果。</returns>
    public static Result Fail(int code, string message) => new() { Code = code, Message = message };
}

/// <summary>统一响应结果（带数据载荷）。</summary>
/// <typeparam name="T">数据载荷类型。</typeparam>
public class Result<T> : Result
{
    /// <summary>响应数据。</summary>
    public T? Data { get; init; }
}
