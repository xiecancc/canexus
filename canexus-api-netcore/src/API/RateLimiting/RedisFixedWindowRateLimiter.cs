using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace API.RateLimiting;

/// <summary>
/// 基于 Redis 原子自增的固定窗口限流器（P1-R2 加固版）。
/// <para>与 <see cref="System.Threading.RateLimiting.FixedWindowRateLimiter"/> 语义对齐：
/// 每分区每窗口最多 <see cref="RedisFixedWindowRateLimiterOptions.PermitLimit"/> 次请求；
/// 计数状态存放在 Redis（键按 分区键+窗口起点 分片），多副本共享同一配额（P1-A5）。</para>
/// <para>原子性（P1-R2）：计数与过期经由「确定性 Lua 脚本」（INCR + 首次 EXPIRE）单次往返原子完成，
/// 消除旧实现 INCR 与条件 EXPIRE 两次调用之间连接中断导致键永不过期（分区被永久锁死）的风险；
/// 脚本仅含确定性命令（不含 TIME），兼容对"非确定性命令后跟写命令"有限制的旧版 Redis——
/// 窗口起点仍由脚本外独立调用服务端 TIME 计算，多副本读同一服务端时钟，避免实例间时钟偏差。</para>
/// <para>异步化（P1-R2）：内核路径全异步（ScriptEvaluateAsync / TimeAsync），不再以同步 IO 阻塞线程池；
/// 同步入口 <see cref="AttemptAcquireCore"/>（中间件正常路径不会走到）走同一脚本的同步求值。</para>
/// </summary>
public sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    /// <summary>
    /// 原子计数脚本（确定性命令，兼容旧版 Redis 的脚本限制）：
    /// INCR 计数；首次计数（=1）时 EXPIRE 设置窗口过期（单位秒），防止键跨窗口残留。
    /// 单次往返原子执行，连接中断不会留下永不过期的计数键。
    /// </summary>
    private const string IncrementScript =
        """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    private readonly RedisFixedWindowRateLimiterOptions _options;
    private long _successfulLeases;
    private long _failedLeases;

    /// <summary>创建 Redis 固定窗口限流器。</summary>
    /// <param name="options">限流参数。</param>
    public RedisFixedWindowRateLimiter(RedisFixedWindowRateLimiterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.PermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RedisFixedWindowRateLimiterOptions.PermitLimit 必须大于 0");
        }

        if (options.ConnectionMultiplexerFactory is null)
        {
            throw new ArgumentException("RedisFixedWindowRateLimiterOptions.ConnectionMultiplexerFactory 不能为空", nameof(options));
        }
    }

    /// <inheritdoc />
    public override TimeSpan? IdleDuration => null;

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics() =>
        new()
        {
            TotalSuccessfulLeases = (int)Interlocked.Read(ref _successfulLeases),
            TotalFailedLeases = (int)Interlocked.Read(ref _failedLeases)
        };

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitCount);
        try
        {
            return AcquireLeaseSync(permitCount);
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return FailOpen(exception);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitCount);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await AcquireLeaseAsync(permitCount, cancellationToken);
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            // Redis 停机不应把业务请求变成 5xx：限流是保护性设施，故障时 fail-open 放行，
            // 由告警暴露限流失效窗口（正确性由认证 / 业务幂等 / 数据库约束兜底）
            return FailOpen(exception);
        }
    }

    /// <summary>判定是否属于后端故障（取消与参数错误须原样上抛，不得被降级掩盖）。</summary>
    /// <param name="exception">捕获到的异常。</param>
    /// <returns>属于后端故障返回 true。</returns>
    private static bool IsBackendFailure(Exception exception)
        => exception is not OperationCanceledException and not ArgumentException;

    /// <summary>后端故障降级：记录告警并返回「已获取」租约。</summary>
    /// <param name="exception">后端故障异常。</param>
    /// <returns>允许通行的租约。</returns>
    private RateLimitLease FailOpen(Exception exception)
    {
        _options.Logger?.LogWarning(exception,
            "Redis 限流后端不可用，已降级放行（fail-open）：策略 {PolicyId} 分区 {PartitionKey}",
            _options.PolicyId, _options.PartitionKey);
        Interlocked.Increment(ref _successfulLeases);
        return new RedisFixedWindowRateLimitLease(true, null);
    }

    /// <summary>异步租约获取（限流中间件的常规路径）：TIME 取窗口起点 → Lua 原子计数。</summary>
    /// <param name="permitCount">本次请求的许可数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租约（允许 / 拒绝 + Retry-After）。</returns>
    private async ValueTask<RateLimitLease> AcquireLeaseAsync(int permitCount, CancellationToken cancellationToken)
    {
        if (permitCount > _options.PermitLimit)
        {
            // 单次请求所需的许可数已超出窗口配额：必然拒绝
            Interlocked.Increment(ref _failedLeases);
            return new RedisFixedWindowRateLimitLease(false, _options.Window);
        }

        var multiplexer = _options.ConnectionMultiplexerFactory();
        var db = multiplexer.GetDatabase();
        var windowSeconds = (long)_options.Window.TotalSeconds;
        if (windowSeconds <= 0)
        {
            throw new InvalidOperationException("RedisFixedWindowRateLimiterOptions.Window 必须大于 0");
        }

        // 窗口起点以 Redis 服务端时间为准（TIME），多副本读同一服务端时钟，避免实例间偏差导致窗口错位；
        // 集群 / 多节点部署取任一已连接节点（节点间时钟由 NTP 保证一致，TIME 仅用于窗口对齐）
        var server = multiplexer.GetServers().FirstOrDefault(candidate => candidate.IsConnected)
                     ?? multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        var serverNow = await server.TimeAsync();
        var unixNow = new DateTimeOffset(serverNow).ToUnixTimeSeconds();
        var windowStart = unixNow - unixNow % windowSeconds;
        var key = $"{_options.KeyPrefix}{_options.PolicyId}:{_options.PartitionKey}:{windowStart}";

        // Lua 原子计数：INCR + 首次 EXPIRE（秒）单次往返，无"计数后断连导致键永不过期"的锁死风险
        var count = (long)(await db.ScriptEvaluateAsync(
            IncrementScript,
            [new RedisKey(key)],
            [windowSeconds + 1]).WaitAsync(cancellationToken))!;

        return BuildLease(count, windowStart, windowSeconds, unixNow);
    }

    /// <summary>同步租约获取（同步 API 兼容路径，中间件常规路径不会走到）：逻辑同异步路径。</summary>
    /// <param name="permitCount">本次请求的许可数。</param>
    /// <returns>租约（允许 / 拒绝 + Retry-After）。</returns>
    private RateLimitLease AcquireLeaseSync(int permitCount)
    {
        if (permitCount > _options.PermitLimit)
        {
            Interlocked.Increment(ref _failedLeases);
            return new RedisFixedWindowRateLimitLease(false, _options.Window);
        }

        var multiplexer = _options.ConnectionMultiplexerFactory();
        var db = multiplexer.GetDatabase();
        var windowSeconds = (long)_options.Window.TotalSeconds;
        if (windowSeconds <= 0)
        {
            throw new InvalidOperationException("RedisFixedWindowRateLimiterOptions.Window 必须大于 0");
        }

        var server = multiplexer.GetServers().FirstOrDefault(candidate => candidate.IsConnected)
                     ?? multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        var serverNow = server.Time();
        var unixNow = new DateTimeOffset(serverNow).ToUnixTimeSeconds();
        var windowStart = unixNow - unixNow % windowSeconds;
        var key = $"{_options.KeyPrefix}{_options.PolicyId}:{_options.PartitionKey}:{windowStart}";

        var count = (long)db.ScriptEvaluate(
            IncrementScript,
            [new RedisKey(key)],
            [windowSeconds + 1])!;

        return BuildLease(count, windowStart, windowSeconds, unixNow);
    }

    /// <summary>按计数结果构造租约：配额内放行；超出配额拒绝并携带窗口剩余时间为 Retry-After。</summary>
    /// <param name="count">窗口内累计计数。</param>
    /// <param name="windowStart">窗口起点（Unix 秒）。</param>
    /// <param name="windowSeconds">窗口长度（秒）。</param>
    /// <param name="unixNow">当前 Unix 秒（服务端时钟）。</param>
    /// <returns>租约。</returns>
    private RateLimitLease BuildLease(long count, long windowStart, long windowSeconds, long unixNow)
    {
        if (count <= _options.PermitLimit)
        {
            Interlocked.Increment(ref _successfulLeases);
            return new RedisFixedWindowRateLimitLease(true, null);
        }

        Interlocked.Increment(ref _failedLeases);
        var retryAfterSeconds = Math.Max(0, windowStart + windowSeconds - unixNow);
        return new RedisFixedWindowRateLimitLease(false, TimeSpan.FromSeconds(retryAfterSeconds));
    }
}
