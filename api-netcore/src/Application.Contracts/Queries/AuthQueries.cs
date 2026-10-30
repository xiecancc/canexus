namespace Application.Contracts.Queries;

using Application.Contracts.Abstractions;
using Application.Contracts.Auth;

/// <summary>获取图形验证码（SVG base64，有效期 5 分钟，一次性校验）。</summary>
public sealed record GetCaptchaQuery : IQuery<CaptchaResult>;

/// <summary>获取当前用户的前端路由树（按角色过滤，仅目录 / 菜单，排除按钮与隐藏项）。</summary>
public sealed record GetRoutesQuery : IQuery<List<RouteVO>>;

/// <summary>获取当前登录用户信息（角色与权限标识，超管权限为 ["*:*:*"]）。</summary>
public sealed record GetUserInfoQuery : IQuery<UserInfoResult>;
