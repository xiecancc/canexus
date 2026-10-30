namespace Application.Queries;

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

/// <summary>获取部门详情查询处理器。</summary>
public sealed class GetDeptDetailQueryHandler : QueryHandler<GetDeptDetailQuery, DeptDetailVO>
{
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetDeptDetailQueryHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper)
    {
        _deptRepository = deptRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<DeptDetailVO> HandleAsync(GetDeptDetailQuery query, CancellationToken cancellationToken)
    {
        var dept = await _deptRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DeptNotFound, $"部门 {query.Id} 不存在");
        return _mapper.Map<DeptDetailVO>(dept);
    }
}

/// <summary>查询部门树处理器。</summary>
public sealed class GetDeptTreeQueryHandler : QueryHandler<GetDeptTreeQuery, List<DeptTreeVO>>
{
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetDeptTreeQueryHandler(IAggregateTreeRepository<SysDept> deptRepository, IMapper mapper)
    {
        _deptRepository = deptRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<List<DeptTreeVO>> HandleAsync(GetDeptTreeQuery query, CancellationToken cancellationToken)
    {
        var depts = await _deptRepository.GetListAsync(cancellationToken: cancellationToken);
        var filtered = depts.Where(d =>
            (string.IsNullOrWhiteSpace(query.Keyword)
                || d.DeptName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (d.Leader != null && d.Leader.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (query.Status == null || d.Status == query.Status.Value)).ToList();

        // 过滤后经聚合基类装配为树（孤儿部门挂到根级不丢失），实体树递归映射为 VO 树
        return _mapper.Map<List<DeptTreeVO>>(SysDept.BuildTree(filtered));
    }
}
