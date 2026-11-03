
using Application.Contracts.Abstractions;
using Application.Contracts.Notices;

namespace Application.Contracts.Commands;
/// <summary>创建通知公告命令（标题必填 ≤ 200、类型 ∈ {1,2}、状态 ∈ {0,1}、内容必填）。</summary>
public sealed record NoticeCreateCommand : AggregateCreateCommand<NoticeInputRequest>
{
    /// <summary>构造创建通知公告命令。</summary>
    /// <param name="request">创建请求。</param>
    public NoticeCreateCommand(NoticeInputRequest request) : base(request) { }
}

/// <summary>删除通知公告命令。</summary>
/// <param name="Id">公告 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record NoticeDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复通知公告命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">公告 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record NoticeRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新通知公告命令（校验规则与创建一致）。</summary>
public sealed record NoticeUpdateCommand : AggregateUpdateCommand<NoticeInputRequest>
{
    /// <summary>构造更新通知公告命令。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public NoticeUpdateCommand(Guid id, Guid version, NoticeInputRequest request) : base(id, version, request) { }
}
