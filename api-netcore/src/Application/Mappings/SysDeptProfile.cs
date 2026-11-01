
using Application.Contracts.Common;
using Application.Contracts.Depts;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>部门模块映射：实体 → 树节点 / 详情（实体树经 <see cref="Domain.Entities.SysDept.BuildTree"/> 装配后，Children 随 AutoMapper 递归映射）；创建 / 更新请求 → 实体。</summary>
public sealed class SysDeptProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysDeptProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysDept, DeptTreeResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysDept, DeptDetailResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<DeptCreateRequest, SysDept>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<DeptUpdateRequest, SysDept>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
