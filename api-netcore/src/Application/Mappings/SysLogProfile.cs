namespace Application.Mappings;

using Application.Contracts.Logs;
using Domain.Entities;

/// <summary>日志模块映射：操作日志 / 登录日志实体 → 列表（日志为只读 append-only 数据，无请求映射）。</summary>
public sealed class SysLogProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysLogProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysOperLog, OperLogListVO>();
        CreateMap<SysLoginLog, LoginLogListVO>();
    }
}
