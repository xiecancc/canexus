
using Application.Contracts.Common;
using Application.Contracts.Jobs;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>定时任务模块映射：任务 / 执行日志实体 → 列表；创建 / 更新请求 → 实体。</summary>
public sealed class SysJobProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysJobProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysJob, JobListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysJobLog, JobLogListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<JobCreateRequest, SysJob>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<JobUpdateRequest, SysJob>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
