using System.Threading.RateLimiting;

namespace API.RateLimiting;

/// <summary>
/// Redis 固定窗口限流租约：携带是否获取配额与拒绝时的 RetryAfter 元数据。
/// <para>RetryAfter 经 <see cref="MetadataName.RetryAfter"/> 暴露，管道层 OnRejected 据此透传 Retry-After 响应头。</para>
/// </summary>
public sealed class RedisFixedWindowRateLimitLease(bool isAcquired, TimeSpan? retryAfter) : RateLimitLease
{
    /// <inheritdoc />
    public override bool IsAcquired => isAcquired;

    /// <inheritdoc />
    public override IEnumerable<string> MetadataNames => retryAfter is null
        ? []
        : [MetadataName.RetryAfter.Name];

    /// <inheritdoc />
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (metadataName == MetadataName.RetryAfter.Name && retryAfter is not null)
        {
            metadata = retryAfter.Value;
            return true;
        }

        metadata = null;
        return false;
    }
}
