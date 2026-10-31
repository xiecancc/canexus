
using Application.Contracts.Depts;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>部门模块映射：实体 → 树节点 / 详情（实体树经 <see cref="Domain.Entities.SysDept.BuildTree"/> 装配后，Children 随 AutoMapper 递归映射）；创建 / 更新请求 → 实体。</summary>
public sealed class SysDeptProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysDeptProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysDept, DeptTreeVO>();
        CreateMap<SysDept, DeptDetailVO>();

        // 请求 → 实体
        CreateMap<DeptCreateRequest, SysDept>();
        CreateMap<DeptUpdateRequest, SysDept>();
    }
}
