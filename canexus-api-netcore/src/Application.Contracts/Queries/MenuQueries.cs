
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Domain.Shared.Enums;

namespace Application.Contracts.Queries;
/// <summary>获取菜单详情。</summary>
/// <param name="Id">菜单 Id。</param>
public sealed record MenuQueryById : AggregateQueryById<MenuDetailResponse>
{
    public MenuQueryById(Guid id) : base(id) { }
}

/// <summary>查询菜单树（keyword 匹配菜单名称 / 权限标识；status 精确；含按钮类型）。</summary>
/// <param name="Keyword">关键字。</param>
/// <param name="Status">状态。</param>
public sealed record MenuQueryTree(string? Keyword, CommonStatus? Status) : Query<List<MenuTreeResponse>>;
