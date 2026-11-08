
using Infrastructure.Shared.Contexts;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Contexts;

/// <summary>
/// 基于 IHttpContextAccessor 的幂等键上下文（单例注册，请求间天然隔离）。
/// <para>长度上限 64 字符：防止超长键撑大缓存键空间；非法键一律视为「未启用幂等」，
/// 不改变原有命令执行语义。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="accessor">HTTP 上下文访问器。</param>
public sealed class HttpIdempotencyContext(IHttpContextAccessor accessor) : IIdempotencyContext
{
    /// <summary>幂等键请求头名称（Stripe / IETF 通用约定）。</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>允许的最大键长度。</summary>
    private const int MaxKeyLength = 64;

    /// <inheritdoc />
    public string? Key
    {
        get
        {
            var raw = accessor.HttpContext?.Request.Headers[HeaderName].ToString();
            return string.IsNullOrWhiteSpace(raw) || raw.Length > MaxKeyLength
                ? null
                : raw.Trim();
        }
    }
}
