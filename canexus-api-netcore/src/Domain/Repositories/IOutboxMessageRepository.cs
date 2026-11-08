
using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Outbox 消息仓储：提供事务内写入、投递侧"认领 / 完成 / 失败"状态机与已处理消息清理。
/// <para>并发语义（P1-B）：投递前必须以条件 UPDATE 认领消息（Pending → Processing + ClaimOwner + LeaseUntil），
/// 同一消息在任一时刻只能被一个副本投递；租约到期后允许其他副本重新认领（防止认领者崩溃导致消息卡死）。
/// 完成 / 失败更新均带 ClaimOwner 条件，避免租约过期后旧认领者覆盖新认领者的结果。</para>
/// <para>幂等语义：Outbox 消息 Id 即幂等键，已被认领或已处理的消息不会被第二个副本再次认领（直接跳过）。
/// 注意：这是"至多一个副本同时处理"的并发幂等；若进程在监听器执行成功后、标记 Processed 前崩溃，
/// 租约到期后仍可能重新投递一次，最终一致性仍依赖监听器自身幂等（至少一次投递语义）。</para>
/// <para>写入（<see cref="InsertAsync"/>）须在业务事务内调用（与命令同事务提交）；
/// 状态更新经独立连接提交，不依赖原事务存活。</para>
/// </summary>
public interface IOutboxMessageRepository
{
    /// <summary>写入消息（随业务事务提交；须在 UnitOfWork / TransactionBehavior 事务内调用）。</summary>
    /// <param name="message">待写入的 Outbox 消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 认领一批可投递消息（Pending 且退避到期，或 Processing 且租约过期），并原子置为 Processing。
    /// <para>每条消息使用条件 UPDATE 抢占，并发副本只会有一个认领成功；返回的集合仅包含认领成功的消息。</para>
    /// </summary>
    /// <param name="batchSize">单批最大条数。</param>
    /// <param name="owner">认领者标识（副本实例）。</param>
    /// <param name="lease">租约时长（到期后可被其他副本重新认领）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认领成功的消息列表（按创建时间升序）。</returns>
    public Task<List<OutboxMessage>> ClaimBatchAsync(int batchSize, string owner, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>尝试认领单条消息（提交后同步投递路径使用）；未抢到或已被处理返回 false。</summary>
    /// <param name="id">消息 Id（幂等键）。</param>
    /// <param name="owner">认领者标识。</param>
    /// <param name="lease">租约时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认领成功返回 true。</returns>
    public Task<bool> TryClaimAsync(Guid id, string owner, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>标记投递成功（独立连接提交；仅对当前认领者持有的 Processing 消息生效）。</summary>
    /// <param name="id">消息 Id。</param>
    /// <param name="owner">认领者标识（条件校验，防止覆盖新认领者结果）。</param>
    /// <param name="processedAt">投递完成时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新成功返回 true；认领已易主 / 消息状态已变（命中 0 行）返回 false，调用方不得视为已投递。</returns>
    public Task<bool> MarkProcessedAsync(Guid id, string owner, DateTime processedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录一次投递失败（独立连接提交）；达到重试上限时置为失败（死信），否则回到 Pending 并按退避时间等待重试。
    /// <para>仅对当前认领者持有的 Processing 消息生效；释放认领字段。</para>
    /// </summary>
    /// <param name="id">消息 Id。</param>
    /// <param name="owner">认领者标识。</param>
    /// <param name="error">失败原因。</param>
    /// <param name="attemptCount">本次为第几次尝试。</param>
    /// <param name="maxAttempts">最大重试次数。</param>
    /// <param name="nextRetryAt">下一次允许重试的时间（退避）；达到上限时忽略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RecordFailureAsync(Guid id, string owner, string error, int attemptCount, int maxAttempts, DateTime nextRetryAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把长期未完成投递的 Pending / Processing 消息转为 Failed（死信），避免退避异常 / 副本崩溃造成记录无限堆积。
    /// 仅处理创建时间早于截止时间的消息；分批执行，单批受 maxRows 限制。
    /// </summary>
    /// <param name="createdBeforeUtc">超期判定截止时间（UTC）。</param>
    /// <param name="reason">写入 LastError 的超期原因。</param>
    /// <param name="maxRows">单批最大处理行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际转为 Failed 的行数。</returns>
    public Task<int> MarkStaleAsFailedAsync(DateTime createdBeforeUtc, string reason, int maxRows, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理终态消息（P1-R3：防止 Outbox 表无限增长；分批删除，单批受 maxRows 限制）：
    /// Processed 且 ProcessedAt 早于对应截止时间，或 Failed 且 CreatedAt 早于对应截止时间。
    /// Pending / Processing 不直接删除，须先经 <see cref="MarkStaleAsFailedAsync"/> 转死信。
    /// </summary>
    /// <param name="processedCutoffUtc">已投递消息的保留截止时间（UTC）；null 表示不清理 Processed。</param>
    /// <param name="failedCutoffUtc">死信消息的保留截止时间（UTC）；null 表示不清理 Failed。</param>
    /// <param name="maxRows">单批最大删除行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除的行数。</returns>
    public Task<int> DeleteCompletedBeforeAsync(DateTime? processedCutoffUtc, DateTime? failedCutoffUtc, int maxRows, CancellationToken cancellationToken = default);
}
