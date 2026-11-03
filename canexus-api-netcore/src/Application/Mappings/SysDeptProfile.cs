using Application.Contracts.Depts;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>部门模块映射：实体 → 树节点 / 详情（实体树经 AggregateTreeBase 的静态 BuildTree 装配后，Children 随 AutoMapper 递归映射）；写入请求 → 实体。</summary>
public sealed class SysDeptProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysDeptProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段；树节点与详情统一挂详情基类）
        CreateMap<SysDept, DeptTreeResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>();
        CreateMap<SysDept, DeptDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<DeptInputRequest, SysDept>(MemberList.None);
    }
}
