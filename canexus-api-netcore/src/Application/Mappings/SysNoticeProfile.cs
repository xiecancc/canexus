using Application.Contracts.Dtos.Common;
using Application.Contracts.Notices;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>通知公告模块映射：实体 → 列表 / 详情 / 最新简项（列表映射忽略正文以减小载荷，详情正文直通）；写入请求 → 实体。</summary>
public sealed class SysNoticeProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysNoticeProfile()
    {
        // 实体 → 列表 / 最新简项（IncludeBase 挂接分页最小根映射链，补齐 Id / Version / CreatedAt / Description）
        CreateMap<SysNotice, NoticePagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>()
            .ForMember(dest => dest.Content, opt => opt.Ignore());
        CreateMap<SysNotice, NoticeLatestResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        // 实体 → 详情（IncludeBase 挂接详情根映射链，补齐租户与审计字段；正文直通）
        CreateMap<SysNotice, NoticeDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<NoticeInputRequest, SysNotice>(MemberList.None);
    }
}
