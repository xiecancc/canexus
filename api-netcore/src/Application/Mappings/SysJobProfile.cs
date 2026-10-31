
using Application.Contracts.Jobs;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>定时任务模块映射：任务 / 执行日志实体 → 列表；创建 / 更新请求 → 实体。</summary>
public sealed class SysJobProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysJobProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysJob, JobListVO>();
        CreateMap<SysJobLog, JobLogListVO>();

        // 请求 → 实体
        CreateMap<JobCreateRequest, SysJob>();
        CreateMap<JobUpdateRequest, SysJob>();
    }
}
