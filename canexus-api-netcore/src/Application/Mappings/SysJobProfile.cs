using Application.Contracts.Dtos.Common;
using Application.Contracts.Jobs;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>定时任务模块映射：任务 / 执行日志实体 → 列表；写入请求 → 实体。</summary>
public sealed class SysJobProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysJobProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysJob, JobPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysJobLog, JobLogPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<JobInputRequest, SysJob>(MemberList.None);
    }
}
