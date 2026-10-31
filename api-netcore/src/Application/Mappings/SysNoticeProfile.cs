
using Application.Contracts.Notices;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>通知公告模块映射：实体 → 列表 / 最新简项（列表映射忽略正文以减小载荷，详情由服务层显式赋值）；创建 / 更新请求 → 实体。</summary>
public sealed class SysNoticeProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysNoticeProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysNotice, NoticeListVO>().ForMember(dest => dest.Content, opt => opt.Ignore());
        CreateMap<SysNotice, NoticeLatestVO>();

        // 请求 → 实体
        CreateMap<NoticeCreateRequest, SysNotice>();
        CreateMap<NoticeUpdateRequest, SysNotice>();
    }
}
