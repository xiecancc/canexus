using Application.Contracts.Dtos.Common;
using Application.Contracts.Online;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>在线会话模块映射：刷新令牌实体 → 在线会话列表行（只读会话视图，无请求映射）。</summary>
public sealed class SysOnlineProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysOnlineProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链补齐 Id / Version / 审计字段；
        // 用户账号 / 昵称 / 部门名由查询处理器批量组装，此处显式忽略以满足配置校验）
        CreateMap<SysRefreshToken, OnlineUserPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>()
            .ForMember(destination => destination.LoginTime, options => options.MapFrom(source => source.CreatedAt))
            .ForMember(destination => destination.UserName, options => options.Ignore())
            .ForMember(destination => destination.NickName, options => options.Ignore())
            .ForMember(destination => destination.DeptName, options => options.Ignore());
    }
}
