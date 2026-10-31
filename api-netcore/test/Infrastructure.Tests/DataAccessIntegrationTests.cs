
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Paging;
using Infrastructure;
using Infrastructure.DataAccess;
using Infrastructure.Shared.Filtering;
using Infrastructure.Shared.Options;
using Infrastructure.Shared.Tenants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.Tests;
/// <summary>
/// 数据访问层集成测试：连接真实 MySQL（默认库 super_admin_test）。
/// <para>连接串可通过环境变量 SUPER_ADMIN_TEST_CONN 覆盖。</para>
/// </summary>
[TestFixture]
public class DataAccessIntegrationTests
{
    /// <summary>默认测试连接串（root 密码为本机约定值）。</summary>
    private const string DefaultConnectionString =
        "server=localhost;port=3306;database=super_admin_test;user id=root;password=***REMOVED***;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none";

    private ServiceProvider _provider = null!;
    private ISqlSugarClient _client = null!;
    private IAggregateRepository<SoftDeleteTestEntity> _softDeleteRepository = null!;
    private IAggregateRepository<MultiTenantTestEntity> _tenantRepository = null!;
    private IAggregateRepository<PlainTestEntity> _plainRepository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ITenantContext _tenantContext = null!;
    private IDataFilter _dataFilter = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_ADMIN_TEST_CONN") ?? DefaultConnectionString;

        // 复用生产注册入口（IConfiguration 重载），内存配置覆盖测试连接串
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connectionString
            })
            .Build();

        var services = new ServiceCollection();
        // 与生产一致：先统一注册全部 Option，再装配数据访问；日志供 SqlSugar 工厂 / 初始化器使用
        services.AddLogging();
        services.RegisterOptions(configuration);
        services.AddDataAccess();

        // 测试实体位于测试程序集：向 Options 管道追加 Configure（与生产配置绑定叠加生效）
        services.AddOptions<DatabaseOption>()
            .Configure(options => options.EntityAssemblies.Add(typeof(DataAccessIntegrationTests).Assembly));

        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<ISqlSugarClient>();
        _softDeleteRepository = _provider.GetRequiredService<IAggregateRepository<SoftDeleteTestEntity>>();
        _tenantRepository = _provider.GetRequiredService<IAggregateRepository<MultiTenantTestEntity>>();
        _plainRepository = _provider.GetRequiredService<IAggregateRepository<PlainTestEntity>>();
        _unitOfWork = _provider.GetRequiredService<IUnitOfWork>();
        _tenantContext = _provider.GetRequiredService<ITenantContext>();
        _dataFilter = _provider.GetRequiredService<IDataFilter>();

        // CodeFirst 建库建表（复用已存在的库表）
        var initializer = _provider.GetRequiredService<DbInitializer>();
        await initializer.InitializeAsync();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _provider.Dispose();
    }

    [SetUp]
    public async Task SetUpAsync()
    {
        // 每个测试前清空数据并重置上下文状态，保证测试独立
        await _client.Ado.ExecuteCommandAsync("DELETE FROM it_soft_delete_entity");
        await _client.Ado.ExecuteCommandAsync("DELETE FROM it_multi_tenant_entity");
        await _client.Ado.ExecuteCommandAsync("DELETE FROM it_plain_entity");
        _tenantContext.TenantId = null;
        _dataFilter.SoftDeleteEnabled = true;
        _dataFilter.TenantFilterEnabled = true;
    }

    [Test]
    public async Task CodeFirst_CreatesDatabaseAndTables()
    {
        // 初始化后三张测试表均应存在（建库在 OneTimeSetUp 的 InitializeAsync 中完成）
        Assert.Multiple(() =>
        {
            Assert.That(_client.DbMaintenance.IsAnyTable("it_soft_delete_entity"), Is.True, "缺少表 it_soft_delete_entity");
            Assert.That(_client.DbMaintenance.IsAnyTable("it_multi_tenant_entity"), Is.True, "缺少表 it_multi_tenant_entity");
            Assert.That(_client.DbMaintenance.IsAnyTable("it_plain_entity"), Is.True, "缺少表 it_plain_entity");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task UnitOfWork_ExceptionRollback_DiscardsInsertedData()
    {
        // 事务内插入后抛出异常：自动回滚，数据不应落库
        Assert.ThrowsAsync<InvalidOperationException>(() => _unitOfWork.RunInTransactionAsync(async () =>
        {
            await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "待回滚" });
            throw new InvalidOperationException("主动失败以触发回滚");
        }));

        var count = await _softDeleteRepository.CountAsync();
        Assert.That(count, Is.EqualTo(0), "回滚后不应存在数据");

        await Task.CompletedTask;
    }

    [Test]
    public async Task UnitOfWork_Commit_PersistsInsertedData()
    {
        // 事务内插入：委托正常结束自动提交，数据应正常落库
        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "待提交" });
            return true;
        });

        var count = await _softDeleteRepository.CountAsync();
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task InsertAndUpdate_FillsAuditFields()
    {
        // 插入：创建人/时间与软删除标记自动填充（无登录用户时创建人为空 Guid）
        var entity = new SoftDeleteTestEntity { Name = "审计实体" };
        var inserted = await _softDeleteRepository.InsertAsync(entity);

        Assert.Multiple(() =>
        {
            Assert.That(inserted.Id, Is.Not.EqualTo(Guid.Empty), "插入时应自动生成 Guid 主键");
            Assert.That(inserted.CreatedAt, Is.Not.EqualTo(default(DateTime)), "创建时间应自动填充");
            Assert.That(inserted.CreatedBy, Is.EqualTo(Guid.Empty), "无登录用户时创建人应为空 Guid");
            Assert.That(inserted.IsDeleted, Is.False, "插入时软删除标记应为 false");
            Assert.That(inserted.UpdatedAt, Is.Null, "新插入实体不应有更新时间");
            Assert.That(inserted.Version, Is.Not.EqualTo(Guid.Empty), "插入时乐观锁版本令牌应自动生成");
        });

        // 更新：更新人/时间自动填充且版本令牌轮换，创建字段不被改动（MySQL datetime 为秒级精度，时间断言放宽到 1 秒内）
        // 先记录更新前版本令牌：SqlSugar OptLock 更新成功后会把轮换后的新版本写回实体实例，
        // 断言「库中版本 != 旧令牌」必须用更新前捕获的值，不能用更新后的实体属性
        var versionBeforeUpdate = inserted.Version;
        inserted.Name = "审计实体-更新";
        await _softDeleteRepository.UpdateAsync(inserted);

        var reloaded = await _softDeleteRepository.GetAsync([it => it.Id == inserted.Id]);
        Assert.That(reloaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.Name, Is.EqualTo("审计实体-更新"));
            Assert.That(reloaded.UpdatedAt, Is.Not.Null, "更新时间应自动填充");
            Assert.That(reloaded.UpdatedBy, Is.EqualTo(Guid.Empty), "无登录用户时更新人应为空 Guid");
            Assert.That(reloaded.CreatedAt, Is.EqualTo(inserted.CreatedAt).Within(TimeSpan.FromSeconds(1)), "创建时间不应被更新操作改动");
            Assert.That(reloaded.Version, Is.Not.EqualTo(Guid.Empty).And.Not.EqualTo(versionBeforeUpdate), "显式乐观锁下更新一次后版本令牌应轮换为新 Guid");
            Assert.That(reloaded.Version, Is.EqualTo(inserted.Version), "更新成功后框架应把轮换的新版本令牌写回实体实例");
        });
    }

    [Test]
    public async Task Update_StaleVersion_ThrowsConcurrencyConflict()
    {
        // 乐观锁：以读取后的旧版本（模拟他人已更新）提交更新应抛并发冲突
        var inserted = await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "乐观锁" });

        // 第二个"会话"基于最新数据更新一次，使库中版本令牌轮换
        var latest = await _softDeleteRepository.GetAsync([it => it.Id == inserted.Id]);
        latest!.Name = "他人已更新";
        await _softDeleteRepository.UpdateAsync(latest);

        // 原持有旧版本令牌的副本再提交：版本令牌不一致触发冲突
        inserted.Name = "旧版本更新";

        Assert.ThrowsAsync<BusinessException>(() => _softDeleteRepository.UpdateAsync(inserted));
        await Task.CompletedTask;
    }

    [Test]
    public async Task SoftDelete_MarkedDeleted_InvisibleByDefault()
    {
        // 软删除：删除后默认查询不可见，但物理行仍存在且标记为已删除
        var inserted = await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "将被删除" });

        await _softDeleteRepository.DeleteAsync(inserted);

        var fetched = await _softDeleteRepository.GetAsync([it => it.Id == inserted.Id]);
        var count = await _softDeleteRepository.CountAsync();
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_soft_delete_entity WHERE IsDeleted = 1"));

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.Null, "软删除后默认查询不应可见");
            Assert.That(count, Is.EqualTo(0), "软删除后计数应为 0");
            Assert.That(physicalCount, Is.EqualTo(1), "物理行应保留且标记 IsDeleted = 1");
        });
    }

    [Test]
    public async Task SoftDelete_DisabledFilter_MakesDeletedVisible()
    {
        // 关闭软删除过滤开关后，已删除数据恢复可见
        var inserted = await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "过滤开关" });
        await _softDeleteRepository.DeleteAsync(inserted);

        _dataFilter.SoftDeleteEnabled = false;
        var count = await _softDeleteRepository.CountAsync();

        Assert.That(count, Is.EqualTo(1), "关闭过滤开关后已删除数据应可见");
    }

    [Test]
    public async Task HardDelete_PlainEntity_PhysicalDelete()
    {
        // 物理删除：行被真正移除，不走软删除墓碑
        var inserted = await _plainRepository.InsertAsync(new PlainTestEntity { Name = "物理删除" });
        await _plainRepository.HardDeleteAsync(inserted);

        var count = await _plainRepository.CountAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task Restore_SoftDeletedEntity_BecomesVisibleAgain()
    {
        // 恢复软删除墓碑：恢复后默认查询重新可见
        var inserted = await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = "将被恢复" });
        await _softDeleteRepository.DeleteAsync(inserted.Id);
        Assert.That(await _softDeleteRepository.CountAsync(), Is.EqualTo(0), "删除后默认不可见");

        await _softDeleteRepository.RestoreAsync(inserted.Id);

        Assert.That(await _softDeleteRepository.CountAsync(), Is.EqualTo(1), "恢复后应重新可见");
        var restored = await _softDeleteRepository.GetAsync([it => it.Id == inserted.Id]);
        Assert.That(restored!.IsDeleted, Is.False);
    }

    [Test]
    public async Task TenantFilter_IsolatesDataByCurrentTenant()
    {
        // 租户过滤钩子：未指定租户时全可见，指定租户后仅可见本租户数据
        var tenant1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tenant2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await _tenantRepository.InsertAsync(new MultiTenantTestEntity { Name = "t1-数据", TenantId = tenant1 });
        await _tenantRepository.InsertAsync(new MultiTenantTestEntity { Name = "t2-数据", TenantId = tenant2 });

        // 未指定租户：两条均可见
        var allCount = await _tenantRepository.CountAsync();
        Assert.That(allCount, Is.EqualTo(2), "未指定租户时应可见全部数据");

        // 指定租户 tenant1：仅可见本租户数据
        _tenantContext.TenantId = tenant1;
        var t1List = await _tenantRepository.GetListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(t1List, Has.Count.EqualTo(1), "租户 tenant1 应只可见本租户数据");
            Assert.That(t1List.Single().TenantId, Is.EqualTo(tenant1));
        });
    }

    [Test]
    public async Task GetPagedAsync_ReturnsCorrectPageAndTotal()
    {
        // 分页：插入 25 条，第 2 页每页 10 条应返回 10 条且总数 25
        for (var i = 1; i <= 25; i++)
        {
            await _softDeleteRepository.InsertAsync(new SoftDeleteTestEntity { Name = $"分页-{i:00}" });
        }

        var result = await _softDeleteRepository.GetPagedAsync(new PagedRequest { PageIndex = 2, PageSize = 10 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Total, Is.EqualTo(25));
            Assert.That(result.PageIndex, Is.EqualTo(2));
            Assert.That(result.PageSize, Is.EqualTo(10));
            Assert.That(result.Items, Has.Count.EqualTo(10));
        });
    }
}
