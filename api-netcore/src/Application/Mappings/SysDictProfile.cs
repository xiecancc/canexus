
using Application.Contracts.Dicts;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>字典模块映射：类型 / 数据实体 → 列表 / 简项；创建 / 更新请求 → 实体。</summary>
public sealed class SysDictProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysDictProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysDictType, DictTypeListVO>();
        CreateMap<SysDictData, DictDataListVO>();
        CreateMap<SysDictData, DictDataSimpleVO>();

        // 请求 → 实体
        CreateMap<DictTypeCreateRequest, SysDictType>();
        CreateMap<DictTypeUpdateRequest, SysDictType>();
        CreateMap<DictDataCreateRequest, SysDictData>();
        CreateMap<DictDataUpdateRequest, SysDictData>();
    }
}
