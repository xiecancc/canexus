using Application.Contracts.Dicts;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>字典模块映射：类型 / 数据实体 → 列表 / 简项；创建 / 更新请求 → 实体。</summary>
public sealed class SysDictProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysDictProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysDictType, DictTypeListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysDictData, DictDataListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysDictData, DictDataSimpleResponse>()
            .IncludeBase<AggregateBase, AggregateListResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<DictTypeCreateRequest, SysDictType>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<DictTypeUpdateRequest, SysDictType>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<DictDataCreateRequest, SysDictData>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<DictDataUpdateRequest, SysDictData>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
