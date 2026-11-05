namespace Domain.Services;
/// <summary>缓存运行信息提供者：读取 L2 分布式缓存（Redis）的运行概要（监控端点消费）。
/// <para>未启用 Redis 或连接失败时返回 <see cref="CacheRuntimeSnapshot.Connected"/> = false 的降级快照，不抛出异常。</para></summary>
public interface ICacheMonitorProvider
{
    /// <summary>读取缓存运行概要（Redis 未启用时返回未连接快照）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存运行概要。</returns>
    public Task<CacheRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>缓存运行概要。</summary>
/// <param name="Connected">Redis 是否连接成功（未启用 Redis 时为 false）。</param>
/// <param name="RedisVersion">Redis 服务器版本（未连接时为 null）。</param>
/// <param name="UptimeDays">Redis 已运行天数（未连接时为 null）。</param>
/// <param name="ConnectedClients">当前连接的客户端数（未连接时为 null）。</param>
/// <param name="UsedMemory">已用内存（人类可读，如 "2.5M"；未连接时为 null）。</param>
/// <param name="MaxMemory">最大内存限制（人类可读；未设置或未连接时为 null）。</param>
/// <param name="TotalKeys">逻辑库键总数 DBSIZE（未连接时为 null）。</param>
/// <param name="KeyspaceHits">键空间命中次数（自启动累计；未连接时为 null）。</param>
/// <param name="KeyspaceMisses">键空间未命中次数（自启动累计；未连接时为 null）。</param>
/// <param name="OpsPerSec">每秒命令执行数（瞬时值；未连接时为 null）。</param>
public sealed record CacheRuntimeSnapshot(
    bool Connected,
    string? RedisVersion = null,
    long? UptimeDays = null,
    long? ConnectedClients = null,
    string? UsedMemory = null,
    string? MaxMemory = null,
    long? TotalKeys = null,
    long? KeyspaceHits = null,
    long? KeyspaceMisses = null,
    long? OpsPerSec = null);
