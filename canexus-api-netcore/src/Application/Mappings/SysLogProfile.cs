using Application.Contracts.Dtos.Common;
using Application.Contracts.Logs;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>日志模块映射：操作日志 / 登录日志实体 → 列表（日志为只读 append-only 数据，无请求映射）。</summary>
public sealed class SysLogProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysLogProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysOperLog, OperLogListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysLoginLog, LoginLogListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
    }
}
