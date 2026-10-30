namespace Application.Contracts.Queries;

using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Domain.Shared.Enums;

/// <summary>获取菜单详情。</summary>
/// <param name="Id">菜单 Id。</param>
public sealed record GetMenuDetailQuery(Guid Id) : IQuery<MenuDetailVO>;

/// <summary>查询菜单树（keyword 匹配菜单名称 / 权限标识；status 精确；含按钮类型）。</summary>
/// <param name="Keyword">关键字。</param>
/// <param name="Status">状态。</param>
public sealed record GetMenuTreeQuery(string? Keyword, CommonStatus? Status) : IQuery<List<MenuTreeVO>>;
