
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Depts;
using Application.Contracts.Queries;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取部门详情查询处理器（标准详情流程，无扩展字段组装）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeptQueryByIdHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper) : AggregateQueryByIdHandler<DeptQueryById, SysDept, DeptDetailResponse>(deptRepository, mapper)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DeptNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "部门";
}

/// <summary>查询部门树处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeptQueryTreeHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper) : QueryHandler<DeptQueryTree, List<DeptTreeResponse>>
{
    /// <inheritdoc />
    protected override async Task<List<DeptTreeResponse>> HandleAsync(DeptQueryTree query, CancellationToken cancellationToken)
    {
        var depts = await deptRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = depts.Where(d =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || d.DeptName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (d.Leader is not null && d.Leader.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status is null || d.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿部门挂到根级不丢失），实体树递归映射为 Response 树
        return mapper.Map<List<DeptTreeResponse>>(SysDept.BuildTree(filtered));
    }
}
