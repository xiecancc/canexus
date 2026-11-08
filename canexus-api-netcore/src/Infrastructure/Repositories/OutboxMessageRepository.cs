
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using SqlSugar;

namespace Infrastructure.Repositories;

/// <summary>
/// 基于 SqlSugar 的 Outbox 消息仓储实现（P1-B：多副本认领 + 租约 + 幂等）。
/// <para>认领使用条件 UPDATE（等价于 SELECT ... FOR UPDATE 的单行抢占语义）：只有把消息从
/// Pending（或租约过期的 Processing）更新为 Processing 且 ClaimOwner 为自己的副本才获得投递权，
/// 并发副本对同一消息只有一个能更新成功。租约到期后允许重新认领，避免副本崩溃消息卡死。</para>
/// <para>完成 / 失败更新带 ClaimOwner 条件：租约过期后新副本重新认领，旧副本迟到的更新不会覆盖结果。</para>
/// <para>写入走环境客户端（随业务事务提交）；认领与状态更新经 CopyNew 独立连接提交——
/// 投递动作发生在原事务提交之后，原事务（含连接）可能已结束。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
public sealed class OutboxMessageRepository(ISqlSugarClient client) : IOutboxMessageRepository
{
    /// <summary>环境客户端（写入随业务事务提交）。</summary>
    private ISqlSugarClient Client { get; } = client;

    /// <inheritdoc />
    public async Task InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await Client.Insertable(message).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<OutboxMessage>> ClaimBatchAsync(int batchSize, string owner, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var claimed = new List<OutboxMessage>(batchSize);

        // 候选人查询（只读）：待处理且退避到期，或处理中但租约已过期
        var candidates = await Client.CopyNew()
            .Queryable<OutboxMessage>()
            .Where(message => (message.Status == OutboxMessageStatus.Pending
                    && (message.NextRetryAt == null || message.NextRetryAt <= now))
                || (message.Status == OutboxMessageStatus.Processing
                    && message.LeaseUntil != null
                    && message.LeaseUntil < now))
            .OrderBy(message => message.CreatedAt, OrderByType.Asc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return claimed;
        }

        // 独立连接复用：逐条条件 UPDATE 抢占；并发副本中只有一条能更新成功
        var db = Client.CopyNew();
        foreach (var candidate in candidates)
        {
            var leaseUntil = now.Add(lease);
            var affected = await db.Updateable<OutboxMessage>()
                .SetColumns(message => new OutboxMessage
                {
                    Status = OutboxMessageStatus.Processing,
                    ClaimOwner = owner,
                    ClaimedAt = now,
                    LeaseUntil = leaseUntil
                })
                .Where(message => message.Id == candidate.Id
                    && ((message.Status == OutboxMessageStatus.Pending
                            && (message.NextRetryAt == null || message.NextRetryAt <= now))
                        || (message.Status == OutboxMessageStatus.Processing
                            && message.LeaseUntil != null
                            && message.LeaseUntil < now)))
                .ExecuteCommandAsync(cancellationToken);

            if (affected > 0)
            {
                // 返回已认领状态的对象，供投递服务计算下一次尝试次数
                candidate.Status = OutboxMessageStatus.Processing;
                candidate.ClaimOwner = owner;
                candidate.ClaimedAt = now;
                candidate.LeaseUntil = leaseUntil;
                claimed.Add(candidate);
            }
        }

        return claimed;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(Guid id, string owner, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(lease);

        // 同一 owner 重复认领视为成功（后台 worker 先认领、投递服务再次确认的场景）
        var affected = await Client.CopyNew()
            .Updateable<OutboxMessage>()
            .SetColumns(message => new OutboxMessage
            {
                Status = OutboxMessageStatus.Processing,
                ClaimOwner = owner,
                ClaimedAt = now,
                LeaseUntil = leaseUntil
            })
            .Where(message => message.Id == id
                && ((message.Status == OutboxMessageStatus.Pending
                        && (message.NextRetryAt == null || message.NextRetryAt <= now))
                    || (message.Status == OutboxMessageStatus.Processing
                        && (message.ClaimOwner == owner
                            || (message.LeaseUntil != null && message.LeaseUntil < now)))))
            .ExecuteCommandAsync(cancellationToken);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> MarkProcessedAsync(Guid id, string owner, DateTime processedAt, CancellationToken cancellationToken = default)
    {
        var affected = await Client.CopyNew()
            .Updateable<OutboxMessage>()
            .SetColumns(message => new OutboxMessage
            {
                ProcessedAt = processedAt,
                Status = OutboxMessageStatus.Processed,
                ClaimOwner = null,
                ClaimedAt = null,
                LeaseUntil = null,
                NextRetryAt = null
            })
            .Where(message => message.Id == id
                && message.Status == OutboxMessageStatus.Processing
                && message.ClaimOwner == owner)
            .ExecuteCommandAsync(cancellationToken);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(Guid id, string owner, string error, int attemptCount, int maxAttempts, DateTime nextRetryAt, CancellationToken cancellationToken = default)
    {
        var status = attemptCount >= maxAttempts ? OutboxMessageStatus.Failed : OutboxMessageStatus.Pending;
        await Client.CopyNew()
            .Updateable<OutboxMessage>()
            .SetColumns(message => new OutboxMessage
            {
                AttemptCount = attemptCount,
                LastError = error,
                Status = status,
                ClaimOwner = null,
                ClaimedAt = null,
                LeaseUntil = null,
                NextRetryAt = status == OutboxMessageStatus.Pending ? nextRetryAt : null
            })
            .Where(message => message.Id == id
                && message.Status == OutboxMessageStatus.Processing
                && message.ClaimOwner == owner)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> MarkStaleAsFailedAsync(DateTime createdBeforeUtc, string reason, int maxRows, CancellationToken cancellationToken = default)
    {
        // 分批：先取超期 Id（独立连接只读），再按 Id 集合条件更新，避免大范围 UPDATE 长事务锁表
        var ids = await Client.CopyNew()
            .Queryable<OutboxMessage>()
            .Where(message => (message.Status == OutboxMessageStatus.Pending
                    || message.Status == OutboxMessageStatus.Processing)
                && message.CreatedAt < createdBeforeUtc)
            .Take(maxRows)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        var error = reason.Length <= 1000 ? reason : reason[..1000];
        return await Client.CopyNew()
            .Updateable<OutboxMessage>()
            .SetColumns(message => new OutboxMessage
            {
                Status = OutboxMessageStatus.Failed,
                LastError = error,
                ClaimOwner = null,
                ClaimedAt = null,
                LeaseUntil = null,
                NextRetryAt = null
            })
            .Where(message => ids.Contains(message.Id)
                && (message.Status == OutboxMessageStatus.Pending || message.Status == OutboxMessageStatus.Processing))
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedBeforeAsync(DateTime? processedCutoffUtc, DateTime? failedCutoffUtc, int maxRows, CancellationToken cancellationToken = default)
    {
        // 两个保留期为独立开关：null 表示该终态不清理，避免用 DateTime.MinValue 生成越界的 SQL 参数
        if (processedCutoffUtc is null && failedCutoffUtc is null)
        {
            return 0;
        }

        // 分批清理：先取目标 Id（独立连接只读），再按 Id 集合删除（避免大范围 DELETE 长事务锁表）
        var query = Client.CopyNew().Queryable<OutboxMessage>();
        if (processedCutoffUtc is not null && failedCutoffUtc is not null)
        {
            var processedCutoff = processedCutoffUtc.Value;
            var failedCutoff = failedCutoffUtc.Value;
            query = query.Where(message =>
                (message.Status == OutboxMessageStatus.Processed
                    && message.ProcessedAt != null
                    && message.ProcessedAt < processedCutoff)
                || (message.Status == OutboxMessageStatus.Failed
                    && message.CreatedAt < failedCutoff));
        }
        else if (processedCutoffUtc is not null)
        {
            var cutoff = processedCutoffUtc.Value;
            query = query.Where(message => message.Status == OutboxMessageStatus.Processed
                && message.ProcessedAt != null
                && message.ProcessedAt < cutoff);
        }
        else
        {
            var cutoff = failedCutoffUtc!.Value;
            query = query.Where(message => message.Status == OutboxMessageStatus.Failed
                && message.CreatedAt < cutoff);
        }

        var ids = await query
            .Take(maxRows)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        return await Client.CopyNew()
            .Deleteable<OutboxMessage>()
            .In(ids)
            .ExecuteCommandAsync(cancellationToken);
    }
}
