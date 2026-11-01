using Application.Contracts.Dtos.Common;
using Application.Contracts.Notices;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>通知公告模块映射：实体 → 列表 / 最新简项（列表映射忽略正文以减小载荷，详情由服务层显式赋值）；创建 / 更新请求 → 实体。</summary>
public sealed class SysNoticeProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysNoticeProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysNotice, NoticeListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>()
            .ForMember(dest => dest.Content, opt => opt.Ignore());
        CreateMap<SysNotice, NoticeLatestResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<NoticeCreateRequest, SysNotice>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<NoticeUpdateRequest, SysNotice>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
