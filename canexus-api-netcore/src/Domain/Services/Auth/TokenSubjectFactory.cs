
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;

namespace Domain.Services.Auth;
/// <summary>令牌主题工厂端口：由用户聚合构建令牌主题（加载启用角色，供 JWT 签发与刷新令牌轮换共用）。</summary>
public interface ITokenSubjectFactory
{
    /// <summary>构建令牌主题：加载用户启用状态的角色集合（Id 与 roleKey）。</summary>
    /// <param name="user">用户实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌主题。</returns>
    public Task<TokenSubject> BuildAsync(SysUser user, CancellationToken cancellationToken = default);
}

/// <summary>令牌主题工厂实现：登录成功与刷新令牌轮换共用同一构建逻辑（原两处处理器逐字重复，收敛于此）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="roleRepository">角色聚合仓储。</param>
/// <param name="userRoleRepository">用户-角色关联仓储。</param>
public sealed class TokenSubjectFactory(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository) : ITokenSubjectFactory
{
    /// <inheritdoc />
    public async Task<TokenSubject> BuildAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == user.Id], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return new TokenSubject(
                user.Id,
                user.UserName,
                user.DeptId,
                [],
                [],
                user.TenantId,
                user.TokenVersion);
        }

        var roles = await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)], cancellationToken: cancellationToken);
        return new TokenSubject(
            user.Id,
            user.UserName,
            user.DeptId,
            roles.Select(r => r.Id).ToList(),
            roles.Select(r => r.RoleKey).ToList(),
            user.TenantId,
            user.TokenVersion);
    }
}
