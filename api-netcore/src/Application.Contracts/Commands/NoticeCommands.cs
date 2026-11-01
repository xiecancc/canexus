
using Application.Contracts.Abstractions;
using Application.Contracts.Notices;

namespace Application.Contracts.Commands;
/// <summary>创建通知公告命令（标题必填 ≤ 200、类型 ∈ {1,2}、状态 ∈ {0,1}、内容必填）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record NoticeCreateCommand : DomainCreateCommand<NoticeCreateRequest>
{
    public NoticeCreateCommand(NoticeCreateRequest request) : base(request) { }
}

/// <summary>删除通知公告命令。</summary>
/// <param name="Id">公告 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record NoticeDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>更新通知公告命令（校验规则与创建一致）。</summary>
/// <param name="Id">公告 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record NoticeUpdateCommand : AggregateUpdateCommand<NoticeUpdateRequest>
{
    public NoticeUpdateCommand(Guid id, NoticeUpdateRequest request) : base(id, request) { }
}
