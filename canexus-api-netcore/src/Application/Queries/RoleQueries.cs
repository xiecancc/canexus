
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取全部启用角色查询处理器（标准下拉流程：仅启用项，按主键升序排序下推）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleQueryDropdownHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper) :
    AggregateQueryDropdownHandler<RoleQueryDropdown, SysRole, RoleDropdownResponse>(roleRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysRole, bool>>? BuildDropdownPredicate() => r => r.Status == CommonStatus.Enabled;

    /// <inheritdoc />
    protected override IDictionary<Expression<Func<SysRole, object>>, bool?>? BuildDropdownOrders() =>
        new Dictionary<Expression<Func<SysRole, object>>, bool?> { [r => r.Id] = true };
}

/// <summary>获取角色详情查询处理器（标准详情流程 + 组装授权菜单 Id 集合）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleQueryDetailHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IMapper mapper) : AggregateQueryDetailHandler<RoleQueryDetail, SysRole, RoleDetailResponse>(roleRepository, mapper)
{
    /// <summary>角色-菜单关联仓储（授权菜单 Id 组装）。</summary>
    private IDomainRepository<SysRoleMenu> RoleMenuRepository { get; } = roleMenuRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.RoleNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "角色";

    /// <inheritdoc />
    protected override async Task EnrichAsync(RoleDetailResponse response, SysRole entity, CancellationToken cancellationToken)
    {
        response.MenuIds = [.. (await RoleMenuRepository.GetListAsync([rm => rm.RoleId == entity.Id], cancellationToken: cancellationToken))
            .Select(rm => rm.MenuId)
            .Distinct()];
    }
}

/// <summary>分页查询角色处理器（标准分页流程：关键字模糊匹配角色名 / 角色键、状态过滤）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleQueryPagedHandler(IAggregateRepository<SysRole> roleRepository, IMapper mapper) : AggregateQueryPagedHandler<RoleQueryPaged, RolePageRequest, SysRole, RoleListResponse>(roleRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysRole, bool>>? BuildPredicate(RolePageRequest request)
    {
        var keyword = NormalizeFilter(request.Keyword);
        var status = request.Status;
        return r =>
            (keyword == null || r.RoleName.Contains(keyword) || r.RoleKey.Contains(keyword))
            && (status == null || r.Status == status.Value);
    }
}
