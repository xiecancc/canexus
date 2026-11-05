namespace Infrastructure.Shared.Options;

/// <summary>
/// 领域事件 Outbox 配置项（绑定配置节 Outbox）。
/// <para>Outbox 保证「事件与业务状态同事务持久化、事务提交后投递」：
/// 提交后投递失败由后台 worker 按 <see cref="PollIntervalSeconds"/> 轮询重试，
/// 超过 <see cref="MaxAttempts"/> 后标记失败（保留记录供排查）。</para>
/// </summary>
public sealed class OutboxOption : OptionBase
{
    /// <summary>是否启用后台投递 worker（关闭时仅保留提交后同步投递，失败记录不自动重试）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>worker 轮询间隔（秒）。</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>单批拉取的最大待处理消息数。</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>单条消息最大投递尝试次数，超过后置为失败状态（死信，保留记录供排查 / 重放）。</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// 认领租约时长（秒）：副本认领消息后持有该时长的处理权；副本崩溃时租约到期后其他副本可重新认领。
    /// 建议大于单条事件监听器的最长处理耗时，避免处理中即被其他副本抢占（默认 60 秒）。
    /// </summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>失败后退避的基础延迟（秒）：第 n 次失败后等待 base * 2^(n-1) 再重试（默认 10 秒）。</summary>
    public int BaseRetryDelaySeconds { get; set; } = 10;

    /// <summary>失败后退避的最大延迟（秒），避免无限增长（默认 600 秒）。</summary>
    public int MaxRetryDelaySeconds { get; set; } = 600;

    /// <summary>
    /// 已成功投递消息的保留天数（P1-R3：防止 Outbox 表无限增长），默认 7 天；0 表示永久保留不清理。
    /// 清理由后台 worker 周期执行（每小时至多一轮，分批删除），仅清理 Status=Processed 的消息，
    /// Pending / Failed 记录永久保留供排查与重试。
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <inheritdoc />
    public override void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        EnsureGreaterThanZero(PollIntervalSeconds, nameof(PollIntervalSeconds), "Outbox:PollIntervalSeconds 必须大于 0");
        EnsureGreaterThanZero(BatchSize, nameof(BatchSize), "Outbox:BatchSize 必须大于 0");
        EnsureGreaterThanZero(MaxAttempts, nameof(MaxAttempts), "Outbox:MaxAttempts 必须大于 0");
        EnsureGreaterThanZero(LeaseSeconds, nameof(LeaseSeconds), "Outbox:LeaseSeconds 必须大于 0");
        EnsureGreaterThanZero(BaseRetryDelaySeconds, nameof(BaseRetryDelaySeconds), "Outbox:BaseRetryDelaySeconds 必须大于 0");
        EnsureGreaterThanZero(MaxRetryDelaySeconds, nameof(MaxRetryDelaySeconds), "Outbox:MaxRetryDelaySeconds 必须大于 0");
        if (MaxRetryDelaySeconds < BaseRetryDelaySeconds)
        {
            throw new ArgumentException("Outbox:MaxRetryDelaySeconds 不得小于 Outbox:BaseRetryDelaySeconds", nameof(MaxRetryDelaySeconds));
        }

        EnsureInRange(RetentionDays, 0, 3_650, nameof(RetentionDays), "Outbox:RetentionDays 必须在 0-3650 天之间（0 = 永久保留）");
    }
}
