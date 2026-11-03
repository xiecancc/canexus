
using System.Linq.Expressions;
using System.Security.Claims;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Shared.Paging;
using Infrastructure.Auth;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Application.Tests;
/// <summary>内存仓储替身：仅覆盖令牌单测所需的仓储行为（插入 / 按条件查询 / 更新），避免依赖真实数据库。</summary>
internal sealed class InMemoryRefreshTokenRepository : ISysRefreshTokenRepository
{
    private readonly List<SysRefreshToken> _store = [];

    /// <summary>存储的数据（供测试断言）。</summary>
    public IReadOnlyList<SysRefreshToken> Store => _store;

    /// <inheritdoc />
    public Task<List<SysRefreshToken>> GetListAsync(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        IDictionary<Expression<Func<SysRefreshToken, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 与 SqlSugar 实现语义对齐：多条件 AND 组合；多重排序按字典顺序应用；orders 为 null 时默认按创建时间倒序
        var query = _store.AsQueryable();
        if (predicates is not null)
        {
            foreach (var predicate in predicates)
            {
                if (predicate is not null)
                {
                    query = query.Where(predicate);
                }
            }
        }

        IOrderedQueryable<SysRefreshToken>? orderedQuery = null;
        if (orders is not null)
        {
            foreach (var (selector, ascending) in orders)
            {
                if (selector is null)
                {
                    continue;
                }

                if (orderedQuery is null)
                {
                    orderedQuery = ascending == true ? query.OrderBy(selector) : query.OrderByDescending(selector);
                }
                else
                {
                    orderedQuery = ascending == true ? orderedQuery.ThenBy(selector) : orderedQuery.ThenByDescending(selector);
                }
            }
        }

        var result = (orderedQuery ?? query.OrderByDescending(e => e.CreatedAt)).ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<SysRefreshToken?> GetAsync(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 与 SqlSugar 实现语义对齐：多条件 AND 组合，取首条（不追加默认排序）
        var query = _store.AsQueryable();
        if (predicates is not null)
        {
            foreach (var predicate in predicates)
            {
                if (predicate is not null)
                {
                    query = query.Where(predicate);
                }
            }
        }

        return Task.FromResult(query.FirstOrDefault());
    }

    /// <inheritdoc />
    public Task<List<TDto>> GetListAsync<TDto>(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        IDictionary<Expression<Func<SysRefreshToken, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => throw new NotSupportedException("令牌单测不使用投影查询");

    /// <inheritdoc />
    public Task<TDto?> GetAsync<TDto>(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => throw new NotSupportedException("令牌单测不使用投影查询");

    /// <inheritdoc />
    public Task<bool> AnyAsync(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 与 SqlSugar 实现语义对齐：多条件 AND 组合后判断存在
        var query = _store.AsQueryable();
        if (predicates is not null)
        {
            foreach (var predicate in predicates)
            {
                if (predicate is not null)
                {
                    query = query.Where(predicate);
                }
            }
        }

        return Task.FromResult(query.Any());
    }

    /// <inheritdoc />
    public Task<PagedResponse<SysRefreshToken>> GetPagedAsync(
        PagedRequest request,
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        IDictionary<Expression<Func<SysRefreshToken, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("令牌单测不使用分页");

    /// <inheritdoc />
    public Task<PagedResponse<TDto>> GetPagedAsync<TDto>(
        PagedRequest request,
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        IDictionary<Expression<Func<SysRefreshToken, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => throw new NotSupportedException("令牌单测不使用分页");

    /// <inheritdoc />
    public Task<bool> InsertAsync(SysRefreshToken entity, CancellationToken cancellationToken = default)
    {
        entity.Id = Guid.NewGuid();
        _store.Add(entity);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(SysRefreshToken entity, CancellationToken cancellationToken = default)
    {
        // 内存对象为同一引用，属性修改即生效；这里同步替换快照保持语义一致
        var index = _store.FindIndex(e => e.Id == entity.Id);
        if (index >= 0)
        {
            _store[index] = entity;
        }

        return Task.FromResult(index >= 0);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.RemoveAll(e => e.Id == id) > 0);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.RemoveAll(e => ids.Contains(e.Id)) > 0);

    /// <inheritdoc />
    public Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        => RestoreAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => RestoreAsync(e => ids.Contains(e.Id), cancellationToken);

    /// <inheritdoc />
    public Task<bool> RestoreAsync(Expression<Func<SysRefreshToken, bool>> predicate, CancellationToken cancellationToken = default)
    {
        // 与 SqlSugar 实现语义对齐：仅解除已删除行的墓碑，返回是否命中
        var restored = 0;
        foreach (var entity in _store.Where(new Func<SysRefreshToken, bool>(predicate.Compile())))
        {
            if (entity.IsDeleted)
            {
                entity.IsDeleted = false;
                restored++;
            }
        }

        return Task.FromResult(restored > 0);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(
        IEnumerable<Expression<Func<SysRefreshToken, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 与 SqlSugar 实现语义对齐：多条件 AND 组合后统计
        var query = _store.AsQueryable();
        if (predicates is not null)
        {
            foreach (var predicate in predicates)
            {
                if (predicate is not null)
                {
                    query = query.Where(predicate);
                }
            }
        }

        return Task.FromResult((long)query.Count());
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Expression<Func<SysRefreshToken, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.RemoveAll(new Predicate<SysRefreshToken>(predicate.Compile())) > 0);

    /// <inheritdoc />
    public Task<bool> InsertAsync(IEnumerable<SysRefreshToken> entities, CancellationToken cancellationToken = default)
    {
        _store.AddRange(entities);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<SysRefreshToken?> GetActiveByTokenAsync(string token, DateTime now, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.FirstOrDefault(t => t.Token == token && t.RevokedAt is null && t.ExpiresAt > now));

    /// <inheritdoc />
    public Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var token in _store.Where(t => t.UserId == userId && t.RevokedAt is null && t.ExpiresAt > revokedAt))
        {
            token.RevokedAt = revokedAt;
            count++;
        }

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> RemoveExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.RemoveAll(t => t.ExpiresAt < now || t.RevokedAt is not null));
}

/// <summary>IOptionsMonitor 测试替身：恒返回固定配置值（供 JwtTokenService 等依赖监视器的服务使用）。</summary>
internal sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    /// <inheritdoc />
    public T CurrentValue => currentValue;

    /// <inheritdoc />
    public T Get(string? name) => currentValue;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>JwtTokenService 单元测试：发放 / 校验（含过期）/ 刷新令牌持久化、活性查询与吊销。</summary>
[TestFixture]
public class JwtTokenServiceTests
{
    private static readonly JwtOption Options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        AccessTokenMinutes = 120,
        RefreshTokenDays = 7,
        Secret = "unit-test-secret-key-0123456789-abcdef"
    };

    private InMemoryRefreshTokenRepository _repository = null!;
    private JwtTokenService _tokenService = null!;

    /// <summary>测试初始化：构建内存刷新令牌仓储与 JWT 令牌服务。</summary>
    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryRefreshTokenRepository();
        _tokenService = new JwtTokenService(new TestOptionsMonitor<JwtOption>(Options), _repository, NullLogger<JwtTokenService>.Instance);
    }

    /// <summary>构造测试令牌主题（固定 Guid 便于断言 claim 内容）。</summary>
    private static TokenSubject CreateSubject() => new(
        UserId: TestUserId,
        UserName: "admin",
        DeptId: TestDeptId,
        RoleIds: [TestRoleId1, TestRoleId2],
        RoleKeys: ["super_admin", "common"],
        TenantId: null);

    private static readonly Guid TestUserId = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid TestDeptId = Guid.Parse("00000000-0000-0000-0000-000000000100");
    private static readonly Guid TestRoleId1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TestRoleId2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>验证生成令牌对返回 accessToken 与 64 位刷新令牌，并将刷新令牌持久化。</summary>
    [Test]
    public async Task GenerateAsync_ReturnsPairAndPersistsRefreshToken()
    {
        var subject = CreateSubject();

        var pair = await _tokenService.GenerateAsync(subject);

        Assert.Multiple(() =>
        {
            Assert.That(pair.AccessToken, Is.Not.Empty);
            Assert.That(pair.RefreshToken, Has.Length.EqualTo(64), "refreshToken 应为 64 位十六进制随机串");
            Assert.That(pair.ExpiresIn, Is.EqualTo(120 * 60L), "expiresIn 应为 accessToken 有效期秒数");
        });

        // refreshToken 已持久化：1 条记录，归属正确且未过期
        Assert.That(_repository.Store, Has.Count.EqualTo(1));
        var persisted = _repository.Store.Single();
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Token, Is.EqualTo(pair.RefreshToken));
            Assert.That(persisted.UserId, Is.EqualTo(TestUserId));
            Assert.That(persisted.ExpiresAt, Is.GreaterThan(DateTime.Now.AddDays(6.5)), "过期时间应按 RefreshTokenDays 配置");
            Assert.That(persisted.RevokedAt, Is.Null);
        });
    }

    /// <summary>验证合法的 accessToken 校验通过且解析出全部声明。</summary>
    [Test]
    public async Task ValidateAccessToken_ValidToken_ReturnsPrincipalWithClaims()
    {
        var pair = await _tokenService.GenerateAsync(CreateSubject());

        var principal = _tokenService.ValidateAccessToken(pair.AccessToken);

        Assert.That(principal, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(principal!.FindFirst("sub")?.Value, Is.EqualTo(TestUserId.ToString()));
            Assert.That(principal.FindFirst("unique_name")?.Value, Is.EqualTo("admin"));
            Assert.That(principal.FindFirst("deptId")?.Value, Is.EqualTo(TestDeptId.ToString()));
            Assert.That(principal.FindAll("roleId").Select(c => c.Value), Is.EqualTo(new[] { TestRoleId1.ToString(), TestRoleId2.ToString() }));
            Assert.That(principal.FindAll("role").Select(c => c.Value), Is.EqualTo(new[] { "super_admin", "common" }));
            Assert.That(principal.Identity?.IsAuthenticated, Is.True);
        });
    }

    /// <summary>验证被篡改的 accessToken 校验返回 null。</summary>
    [Test]
    public async Task ValidateAccessToken_TamperedToken_ReturnsNull()
    {
        var pair = await _tokenService.GenerateAsync(CreateSubject());

        // 篡改签名段（替换为无效字符）
        var parts = pair.AccessToken.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.invalid-signature";

        Assert.That(_tokenService.ValidateAccessToken(tampered), Is.Null);
    }

    /// <summary>验证已过期的 accessToken 校验返回 null。</summary>
    [Test]
    public void ValidateAccessToken_ExpiredToken_ReturnsNull()
    {
        // 直接构造 5 分钟前已过期的合法签名 token，验证生命周期校验
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Options.Secret)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var expiredToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: Options.Issuer,
            audience: Options.Audience,
            claims: new[] { new Claim("sub", "42"), new Claim("unique_name", "admin") },
            notBefore: DateTime.Now.AddMinutes(-10).ToUniversalTime(),
            expires: DateTime.Now.AddMinutes(-5).ToUniversalTime(),
            signingCredentials: credentials);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(expiredToken);

        Assert.That(_tokenService.ValidateAccessToken(token), Is.Null, "过期 token 应校验失败返回 null");
    }

    /// <summary>验证刷新令牌查询的状态机：活跃可查、吊销后不可查、不存在返回 null。</summary>
    [Test]
    public async Task GetActiveRefreshTokenAsync_StateMachine()
    {
        var pair = await _tokenService.GenerateAsync(CreateSubject());

        // 活跃：可查
        var active = await _tokenService.GetActiveRefreshTokenAsync(pair.RefreshToken);
        Assert.That(active, Is.Not.Null);
        Assert.That(active!.UserId, Is.EqualTo(TestUserId));

        // 吊销后：不可查
        await _tokenService.RevokeAsync(pair.RefreshToken);
        Assert.That(await _tokenService.GetActiveRefreshTokenAsync(pair.RefreshToken), Is.Null, "已吊销令牌不应再返回");

        // 不存在：null
        Assert.That(await _tokenService.GetActiveRefreshTokenAsync("unknown-token"), Is.Null);
    }

    /// <summary>验证已过期的刷新令牌查询返回 null。</summary>
    [Test]
    public async Task GetActiveRefreshTokenAsync_Expired_ReturnsNull()
    {
        // 直接向内存仓储插入一条已过期的刷新令牌
        await _repository.InsertAsync(new SysRefreshToken
        {
            Token = "expired-refresh-token",
            UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ExpiresAt = DateTime.Now.AddMinutes(-1)
        });

        Assert.That(await _tokenService.GetActiveRefreshTokenAsync("expired-refresh-token"), Is.Null, "已过期令牌不应返回");
    }

    /// <summary>验证重复吊销同一刷新令牌是幂等的，不会改动首次吊销时间。</summary>
    [Test]
    public async Task RevokeAsync_IsIdempotent()
    {
        var pair = await _tokenService.GenerateAsync(CreateSubject());

        await _tokenService.RevokeAsync(pair.RefreshToken);
        var firstRevokedAt = _repository.Store.Single().RevokedAt;
        Assert.That(firstRevokedAt, Is.Not.Null);

        // 重复吊销不应改动吊销时间（幂等）
        await _tokenService.RevokeAsync(pair.RefreshToken);
        Assert.That(_repository.Store.Single().RevokedAt, Is.EqualTo(firstRevokedAt));
    }
}

/// <summary>BCryptPasswordHasher 单元测试。</summary>
[TestFixture]
public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new(NullLogger<BCryptPasswordHasher>.Instance);

    /// <summary>验证哈希结果含 BCrypt 盐前缀且可通过明文验证。</summary>
    [Test]
    public void Hash_ProducesVerifiableHashWithSalt()
    {
        var hash = _hasher.Hash("admin123");

        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.Not.EqualTo("admin123"));
            Assert.That(hash, Does.StartWith("$2"), "BCrypt 哈希应带 $2 前缀（含成本因子与盐）");
            Assert.That(_hasher.Verify("admin123", hash), Is.True);
        });
    }

    /// <summary>验证同一明文两次哈希因自动加盐产生不同结果。</summary>
    [Test]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        // 自动加盐：同一明文两次哈希结果不同，但均可验证通过
        var hash1 = _hasher.Hash("admin123");
        var hash2 = _hasher.Hash("admin123");

        Assert.Multiple(() =>
        {
            Assert.That(hash1, Is.Not.EqualTo(hash2));
            Assert.That(_hasher.Verify("admin123", hash1), Is.True);
            Assert.That(_hasher.Verify("admin123", hash2), Is.True);
        });
    }

    /// <summary>验证密码错误时校验返回 false。</summary>
    [Test]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("admin123");

        Assert.That(_hasher.Verify("wrong-password", hash), Is.False);
    }

    /// <summary>验证非法或空哈希串校验返回 false 而不抛异常。</summary>
    [Test]
    public void Verify_InvalidHash_ReturnsFalseWithoutThrow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_hasher.Verify("admin123", "not-a-bcrypt-hash"), Is.False);
            Assert.That(_hasher.Verify("admin123", string.Empty), Is.False);
        });
    }
}
