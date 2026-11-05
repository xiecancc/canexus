
using Application.Contracts.Dtos.Common;
using Application.Contracts.Posts;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>岗位模块映射：实体 → 列表 / 详情 / 下拉简项；写入请求 → 实体。</summary>
public sealed class SysPostProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysPostProfile()
    {
        // 实体 → 列表 / 下拉简项（IncludeBase 挂接分页最小根映射链，补齐 Id / Version / CreatedAt / Description）
        CreateMap<SysPost, PostPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysPost, PostDropdownResponse>();
        // 实体 → 详情（IncludeBase 挂接详情根映射链，补齐租户与审计字段）
        CreateMap<SysPost, PostDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>();

        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<PostInputRequest, SysPost>(MemberList.None);
    }
}
