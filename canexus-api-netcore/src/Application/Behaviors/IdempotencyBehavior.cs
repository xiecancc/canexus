
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Contracts.Abstractions;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Contexts;
using Infrastructure.Shared.Locking;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

/// <summary>
/// 写命令幂等管道：仅在请求携带 <c>Idempotency-Key</c> 时生效，按「用户 + 命令类型 + 键」
/// 缓存成功结果 24 小时（HybridCache 双层缓存），重复提交直接返回首次结果，不重复执行处理器 / 事务 / 审计。
/// <para>并发保护复用现有 <see cref="IDistributedLock"/>：拿不到锁时降级为无锁路径（可用性优先），
/// 与项目既有锁语义一致。同一键用于不同请求参数时抛 40005 冲突，避免误复用。
/// 不含请求头的调用完全不受影响，前端无需改造。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="cache">双层缓存。</param>
/// <param name="idempotencyContext">幂等键上下文。</param>
/// <param name="currentUser">当前用户上下文（隔离不同用户的键空间）。</param>
/// <param name="distributedLock">分布式锁。</param>
/// <param name="logger">日志器。</param>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    ICache cache,
    IIdempotencyContext idempotencyContext,
    IUserContext currentUser,
    IDistributedLock distributedLock,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>幂等结果保留时长（L2）。</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var key = idempotencyContext.Key;
        if (request is not ICommand || string.IsNullOrEmpty(key))
        {
            return await next();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = $"idempotency:{currentUser.UserId?.ToString() ?? "anonymous"}:{typeof(TRequest).Name}:{key}";
        var fingerprint = ComputeFingerprint(request);

        var cached = await ReadCachedEnvelopeAsync(cacheKey, fingerprint, cancellationToken);
        if (cached is not null)
        {
            return cached.Value!;
        }

        // 锁失败（Redis 抖动 / 并发抢占超时）降级为无锁路径：正确性由最终结果缓存兜底
        var handle = await distributedLock.AcquireAsync(
            cacheKey, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), cancellationToken);
        try
        {
            // 双检：等待锁期间可能已有副本完成写入
            cached = await ReadCachedEnvelopeAsync(cacheKey, fingerprint, cancellationToken);
            if (cached is not null)
            {
                return cached.Value!;
            }

            var response = await next();
            await cache.SetAsync(
                cacheKey,
                new IdempotencyEnvelope<TResponse> { Fingerprint = fingerprint, Value = response },
                new CacheTtlProfile(TimeSpan.FromMinutes(5), Retention),
                cancellationToken);
            return response;
        }
        finally
        {
            if (handle is not null)
            {
                await handle.DisposeAsync();
            }
        }
    }

    /// <summary>读取缓存信封；命中时校验请求指纹，返回 null 表示未命中（信封为引用类型，可区分缓存的 false / 0）。</summary>
    /// <param name="cacheKey">缓存键。</param>
    /// <param name="fingerprint">当前请求指纹。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存信封；未命中返回 null。</returns>
    private async Task<IdempotencyEnvelope<TResponse>?> ReadCachedEnvelopeAsync(string cacheKey, string? fingerprint, CancellationToken cancellationToken)
    {
        var envelope = await cache.GetAsync<IdempotencyEnvelope<TResponse>>(cacheKey, cancellationToken);
        if (envelope is null)
        {
            return null;
        }

        if (envelope.Fingerprint is { } cachedFingerprint
            && fingerprint is { } currentFingerprint
            && !string.Equals(cachedFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            throw new BusinessException(
                ErrorCodes.CommonConcurrencyConflict,
                "该幂等键已用于不同的请求参数，请更换 Idempotency-Key 后重试");
        }

        logger.LogInformation("写命令命中幂等缓存，跳过重复执行：{CacheKey}", cacheKey);
        return envelope;
    }

    /// <summary>计算请求参数指纹（JSON + SHA-256）；含 Stream 等不可序列化载荷时返回 null，降级为仅按键判重。</summary>
    /// <param name="request">请求实例。</param>
    /// <returns>指纹；无法序列化时为 null。</returns>
    private static string? ComputeFingerprint(TRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException)
        {
            // 导入命令等携带 Stream / IFormFile 的请求无法 JSON 序列化，指纹降级不阻断业务
            return null;
        }
    }
}

/// <summary>幂等结果信封：用引用类型区分「未命中」与「缓存中的值类型结果（如 false / 0）」。</summary>
/// <typeparam name="T">命令响应类型。</typeparam>
public sealed class IdempotencyEnvelope<T>
{
    /// <summary>请求参数指纹（不可序列化载荷时为 null）。</summary>
    public string? Fingerprint { get; set; }

    /// <summary>首次执行的响应结果。</summary>
    public T? Value { get; set; }
}
