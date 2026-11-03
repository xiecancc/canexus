using Application.Contracts.Abstractions;
using Application.Contracts.Monitor;

namespace Application.Contracts.Queries;
/// <summary>查询服务器与进程运行信息（含调度器概要）。</summary>
public sealed record ServerMonitorQuery : Request<ServerMonitorResponse>;
