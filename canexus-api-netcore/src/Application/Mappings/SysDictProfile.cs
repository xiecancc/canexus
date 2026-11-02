using Application.Contracts.Dicts;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>字典模块映射：类型 / 数据实体 → 列表 / 简项；写入请求 → 实体。</summary>
public sealed class SysDictProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysDictProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysDictType, DictTypePagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysDictData, DictDataPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysDictData, DictDataDropdownResponse>()
            .IncludeBase<AggregateBase, AggregateDropdownResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<DictTypeInputRequest, SysDictType>(MemberList.None);
        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<DictDataInputRequest, SysDictData>(MemberList.None);
    }
}
