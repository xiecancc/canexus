using Application.Contracts.Abstractions;
using Application.Contracts.Monitor;

namespace Application.Contracts.Queries;
/// <summary>查询服务器与进程运行信息（含调度器概要）。</summary>
public sealed record ServerMonitorQuery : Request<ServerMonitorResponse>;

/// <summary>查询缓存运行信息（Redis 服务器概要 + TTL 配置快照；未启用 Redis 时降级输出配置快照）。</summary>
public sealed record CacheMonitorQuery : Request<CacheMonitorResponse>;
