
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Depts;
using Application.Contracts.Queries;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取部门详情查询处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetDeptDetailQueryHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper) : QueryHandler<GetDeptDetailQuery, DeptDetailVO>
{
    /// <inheritdoc />
    protected override async Task<DeptDetailVO> HandleAsync(GetDeptDetailQuery query, CancellationToken cancellationToken)
    {
        var dept = await deptRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {query.Id} 不存在");
        return mapper.Map<DeptDetailVO>(dept);
    }
}

/// <summary>查询部门树处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetDeptTreeQueryHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper) : QueryHandler<GetDeptTreeQuery, List<DeptTreeVO>>
{
    /// <inheritdoc />
    protected override async Task<List<DeptTreeVO>> HandleAsync(GetDeptTreeQuery query, CancellationToken cancellationToken)
    {
        var depts = await deptRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = depts.Where(d =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || d.DeptName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (d.Leader is not null && d.Leader.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status is null || d.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿部门挂到根级不丢失），实体树递归映射为 VO 树
        return mapper.Map<List<DeptTreeVO>>(SysDept.BuildTree(filtered));
    }
}
