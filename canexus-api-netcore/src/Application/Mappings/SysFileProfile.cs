using Application.Contracts.Dtos.Common;
using Application.Contracts.Files;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>文件存储模块映射：实体 → 分页 / 详情响应（写入走命令处理器显式构造，不经载荷映射）。</summary>
public sealed class SysFileProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysFileProfile()
    {
        // 实体 → 分页项（IncludeBase 挂接分页最小根映射链，补齐 Id / Version / CreatedAt / Description）
        CreateMap<SysFile, FilePagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 实体 → 详情（IncludeBase 挂接详情根映射链，补齐租户与审计字段；RelativePath 供控制器读流）
        CreateMap<SysFile, FileDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>();
    }
}
