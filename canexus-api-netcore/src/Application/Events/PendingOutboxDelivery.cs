using Domain.Entities;

namespace Application.Events;

/// <summary>
/// 待投递 Outbox 消息队列（请求作用域内收集）：领域事件发布器在业务事务内写入消息并加入队列，
/// 事务提交成功后由 TransactionBehavior 统一排空投递——保证「事件持久化与业务同事务、
/// 投递发生在提交之后」。
/// </summary>
public sealed class PendingOutboxDelivery
{
    private readonly List<OutboxMessage> _messages = [];

    /// <summary>加入待投递消息。</summary>
    /// <param name="message">已随业务事务持久化的 Outbox 消息。</param>
    public void Add(OutboxMessage message)
    {
        _messages.Add(message);
    }

    /// <summary>排空全部待投递消息（取出后清空队列）。</summary>
    /// <returns>待投递消息快照。</returns>
    public IReadOnlyList<OutboxMessage> Drain()
    {
        var snapshot = _messages.ToArray();
        _messages.Clear();
        return snapshot;
    }
}
