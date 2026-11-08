using API;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Infrastructure.Shared.Tenants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 写入侧租户补偿回归（2.6 第 5 项 / V-04）：QueryFilter 不作用于 Updateable / Deleteable，
/// 因此 AggregateRepository 的批量软删 / 恢复必须显式追加 TenantId 条件。
/// 本用例验证「A 租户上下文下批量软删 A、B 两个用户，B 不受影响」。
/// </summary>
[TestFixture]
public class TenantWriteScopeTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static ISqlSugarClient _db = null!;

    /// <summary>启动多租户开启的测试宿主（复用真实 DI 与隔离测试库）。</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new ApiTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["RateLimit:Enabled"] = "false",
                        ["MultiTenancy:Enabled"] = "true"
                    });
                });
            });

        _ = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
    }

    /// <summary>释放宿主资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    /// <summary>A 租户上下文下批量软删不得越界修改 B 租户的行。</summary>
    [Test]
    public async Task BatchSoftDelete_UnderTenantA_DoesNotTouchTenantB()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userA = new SysUser
        {
            UserName = "wsc-a-" + suffix,
            NickName = "写入范围 A",
            PasswordHash = "x",
            TenantId = tenantA,
            Status = CommonStatus.Enabled
        };
        var userB = new SysUser
        {
            UserName = "wsc-b-" + suffix,
            NickName = "写入范围 B",
            PasswordHash = "x",
            TenantId = tenantB,
            Status = CommonStatus.Enabled
        };
        await _db.Insertable(userA).ExecuteCommandAsync();
        await _db.Insertable(userB).ExecuteCommandAsync();

        var tenantContext = _factory.Services.GetRequiredService<ITenantContext>();
        try
        {
            tenantContext.TenantId = tenantA;
            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAggregateRepository<SysUser>>();
            await repository.DeleteAsync([userA.Id, userB.Id]);

            var rowA = await _db.Queryable<SysUser>().ClearFilter<SysUser>()
                .Where(u => u.Id == userA.Id).FirstAsync();
            var rowB = await _db.Queryable<SysUser>().ClearFilter<SysUser>()
                .Where(u => u.Id == userB.Id).FirstAsync();

            Assert.Multiple(() =>
            {
                Assert.That(rowA?.IsDeleted, Is.True, "A 租户用户应被批量软删");
                Assert.That(rowB?.IsDeleted, Is.False, "写侧必须显式补租户条件，B 租户用户不得被越界软删");
            });
        }
        finally
        {
            tenantContext.TenantId = null;
            _db.Deleteable<SysUser>().Where(u => u.Id == userA.Id || u.Id == userB.Id).ExecuteCommand();
        }
    }
}
