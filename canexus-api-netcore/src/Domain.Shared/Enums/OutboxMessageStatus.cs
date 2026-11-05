namespace Domain.Shared.Enums;

/// <summary>Outbox 消息处理状态：待处理（可重试）→ 处理中（已被某副本认领）→ 已处理 / 失败（超过重试上限）。</summary>
public enum OutboxMessageStatus
{
    /// <summary>待处理：尚未投递或投递失败等待重试（NextRetryAt 未到期时不会被认领）。</summary>
    Pending = 0,

    /// <summary>已处理：事件已成功投递给全部监听器。</summary>
    Processed = 1,

    /// <summary>失败（死信）：超过最大重试次数，保留记录供人工排查与重放。</summary>
    Failed = 2,

    /// <summary>处理中：已被某副本认领（ClaimOwner）并持有租约（LeaseUntil），租约到期后可被重新认领。</summary>
    Processing = 3
}
