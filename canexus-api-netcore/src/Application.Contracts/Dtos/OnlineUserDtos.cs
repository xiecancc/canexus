using Application.Contracts.Dtos.Common;
using Domain.Shared.Paging;

namespace Application.Contracts.Online;
/// <summary>在线会话分页查询请求（GET /api/monitor/online/page）。</summary>
public class OnlineUserPageRequest : AggregatePagedRequest
{
    /// <summary>登录账号：模糊匹配（按用户名过滤）。</summary>
    public string? UserName { get; set; }

    /// <summary>登录 IP：模糊匹配。</summary>
    public string? Ip { get; set; }
}

/// <summary>在线会话列表行（分页数据项；一条活跃 refreshToken 记录代表一个在线会话）。</summary>
public class OnlineUserPagedResponse : AggregatePagedResponse
{
    /// <summary>登录账号。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>用户昵称。</summary>
    public string? NickName { get; set; }

    /// <summary>部门名称。</summary>
    public string? DeptName { get; set; }

    /// <summary>签发来源 IP 地址。</summary>
    public string? Ip { get; set; }

    /// <summary>签发来源 User-Agent 原文。</summary>
    public string? UserAgent { get; set; }

    /// <summary>登录（令牌签发）时间。</summary>
    public DateTime LoginTime { get; set; }

    /// <summary>会话过期时间（refreshToken 到期时间）。</summary>
    public DateTime ExpiresAt { get; set; }
}
