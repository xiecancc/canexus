
using Application.Contracts.Abstractions;
using Application.Contracts.Depts;
using Domain.Shared.Enums;

namespace Application.Contracts.Queries;
/// <summary>获取部门详情。</summary>
public sealed record DeptQueryDetail : AggregateQueryDetail<DeptDetailResponse>
{
    /// <summary>构造部门详情查询。</summary>
    /// <param name="id">部门 Id。</param>
    public DeptQueryDetail(Guid id) : base(id) { }
}

/// <summary>查询部门树（keyword 匹配部门名称 / 负责人；status 精确）。</summary>
/// <param name="Keyword">关键字。</param>
/// <param name="Status">状态。</param>
public sealed record DeptQueryTree(string? Keyword, CommonStatus? Status) : Request<List<DeptTreeResponse>>;
