using MediatR;

namespace Application.Contracts.Abstractions;

/// <summary>
/// 敏感结果标记：标识响应体含令牌、凭据等不可写入缓存的敏感数据。
/// <para>幂等管道（<c>IdempotencyBehavior</c>）会把成功响应原样缓存 24 小时（HybridCache L2），
/// 用于返回首次执行结果。实现本标记的响应类型（当前为 <c>TokenPairDto</c>，含 JWT 与刷新令牌）
/// 一律跳过整条幂等路径：既禁止缓存明文令牌，也避免登录 / 刷新这类带验证码校验、失败计数与
/// 令牌轮换副作用的命令被复用首次结果。</para>
/// <para>新增含凭据的响应类型时实现本标记即可自动纳入保护，无需改动管道。</para>
/// </summary>
/// <remarks>刻意不继承 <see cref="IRequest"/>：本标记仅供管道做响应类型判定，不参与 MediatR 分发。</remarks>
public interface ISensitiveResult
{
}
