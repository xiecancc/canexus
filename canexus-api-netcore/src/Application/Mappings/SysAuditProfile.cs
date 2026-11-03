using Application.Contracts.Audit;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>审计模块映射：实体变更日志 → 列表行（只读 append-only 数据，无请求映射）。</summary>
public sealed class SysAuditProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysAuditProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysEntityChangeLog, EntityChangeLogPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
    }
}
