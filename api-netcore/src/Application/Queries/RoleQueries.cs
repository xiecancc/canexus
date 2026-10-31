
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取全部启用角色查询处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetAllEnabledRolesQueryHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper) : QueryHandler<GetAllEnabledRolesQuery, List<RoleSimpleVO>>
{
    /// <inheritdoc />
    protected override async Task<List<RoleSimpleVO>> HandleAsync(GetAllEnabledRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        return [.. roles
            .OrderBy(r => r.Id)
            .Select(r => mapper.Map<RoleSimpleVO>(r))];
    }
}

/// <summary>获取角色详情查询处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetRoleDetailQueryHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IMapper mapper) : QueryHandler<GetRoleDetailQuery, RoleDetailVO>
{
    /// <inheritdoc />
    protected override async Task<RoleDetailVO> HandleAsync(GetRoleDetailQuery query, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetAsync([it => it.Id == query.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {query.Id} 不存在");
        var detail = mapper.Map<RoleDetailVO>(role);
        detail.MenuIds = [.. (await roleMenuRepository.GetListAsync([rm => rm.RoleId == query.Id], cancellationToken: cancellationToken))
            .Select(rm => rm.MenuId)
            .Distinct()];
        return detail;
    }
}

/// <summary>分页查询角色处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class GetRolePageQueryHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper) : QueryHandler<GetRolePageQuery, PagedResult<RoleListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<RoleListVO>> HandleAsync(GetRolePageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        var status = request.Status;
        Expression<Func<SysRole, bool>> predicate = r =>
            (keyword == null || r.RoleName.Contains(keyword) || r.RoleKey.Contains(keyword))
            && (status == null || r.Status == status.Value);

        var page = await roleRepository.GetPagedAsync(request, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(role => mapper.Map<RoleListVO>(role)).ToList();
        return PagedResult<RoleListVO>.Create(items, page.Total, request);
    }
}
