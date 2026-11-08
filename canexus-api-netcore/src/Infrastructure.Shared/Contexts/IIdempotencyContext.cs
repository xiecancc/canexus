namespace Infrastructure.Shared.Contexts;

/// <summary>
/// 幂等键上下文端口：读取当前请求携带的 <c>Idempotency-Key</c>。
/// <para>应用层据此按「用户 + 命令类型 + 键」复用写命令结果，HTTP 实现位于 Infrastructure
/// （经 IHttpContextAccessor 按请求隔离）；后台作业无请求上下文时返回 null，即不启用幂等。</para>
/// </summary>
public interface IIdempotencyContext
{
    /// <summary>当前请求的幂等键；缺失、空白或超过 64 字符时为 null（不启用幂等）。</summary>
    public string? Key { get; }
}
