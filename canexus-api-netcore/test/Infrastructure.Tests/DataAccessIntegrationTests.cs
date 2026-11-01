
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
        _ = await _softDeleteRepository.InsertAsync(entity);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.Not.EqualTo(Guid.Empty), "插入时应自动生成 Guid 主键");
            Assert.That(entity.CreatedAt, Is.Not.EqualTo(default(DateTime)), "创建时间应自动填充");
            Assert.That(entity.CreatedBy, Is.EqualTo(Guid.Empty), "无登录用户时创建人应为空 Guid");
            Assert.That(entity.IsDeleted, Is.False, "插入时软删除标记应为 false");
            Assert.That(entity.UpdatedAt, Is.Null, "新插入实体不应有更新时间");
            Assert.That(entity.Version, Is.Not.EqualTo(Guid.Empty), "插入时乐观锁版本令牌应自动生成");
        });

        // 更新：更新人/时间自动填充且版本令牌轮换，创建字段不被改动（MySQL datetime 为秒级精度，时间断言放宽到 1 秒内）
        // 先记录更新前版本令牌：SqlSugar OptLock 更新成功后会把轮换后的新版本写回实体实例，
        // 断言「库中版本 != 旧令牌」必须用更新前捕获的值，不能用更新后的实体属性
        var versionBeforeUpdate = entity.Version;
        entity.Name = "审计实体-更新";
        await _softDeleteRepository.UpdateAsync(entity);

        var reloaded = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        Assert.That(reloaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.Name, Is.EqualTo("审计实体-更新"));
            Assert.That(reloaded.UpdatedAt, Is.Not.Null, "更新时间应自动填充");
            Assert.That(reloaded.UpdatedBy, Is.Null, "无登录用户时更新人应为 null（可空语义，与按列更新口径一致）");
            Assert.That(reloaded.CreatedAt, Is.EqualTo(entity.CreatedAt).Within(TimeSpan.FromSeconds(1)), "创建时间不应被更新操作改动");
            Assert.That(reloaded.Version, Is.Not.EqualTo(Guid.Empty).And.Not.EqualTo(versionBeforeUpdate), "显式乐观锁下更新一次后版本令牌应轮换为新 Guid");
            Assert.That(reloaded.Version, Is.EqualTo(entity.Version), "更新成功后框架应把轮换的新版本令牌写回实体实例");
        });
    }

    [Test]
    public async Task Update_StaleVersion_ThrowsConcurrencyConflict()
    {
        // 乐观锁：以读取后的旧版本（模拟他人已更新）提交更新应抛并发冲突
        var entity = new SoftDeleteTestEntity { Name = "乐观锁" };
        _ = await _softDeleteRepository.InsertAsync(entity);

        // 第二个"会话"基于最新数据更新一次，使库中版本令牌轮换
        var latest = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        latest!.Name = "他人已更新";
        await _softDeleteRepository.UpdateAsync(latest);

        // 原持有旧版本令牌的副本再提交：版本令牌不一致触发冲突
        entity.Name = "旧版本更新";

        Assert.ThrowsAsync<BusinessException>(() => _softDeleteRepository.UpdateAsync(entity));
        await Task.CompletedTask;
    }

    [Test]
    public async Task SoftDelete_MarkedDeleted_InvisibleByDefault()
    {
        // 软删除：删除后默认查询不可见，但物理行仍存在且标记为已删除
        var entity = new SoftDeleteTestEntity { Name = "将被删除" };
        _ = await _softDeleteRepository.InsertAsync(entity);

        await _softDeleteRepository.DeleteAsync(entity.Id);

        var fetched = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
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
        var entity = new SoftDeleteTestEntity { Name = "过滤开关" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        await _softDeleteRepository.DeleteAsync(entity.Id);

        _dataFilter.SoftDeleteEnabled = false;
        var count = await _softDeleteRepository.CountAsync();

        Assert.That(count, Is.EqualTo(1), "关闭过滤开关后已删除数据应可见");
    }

    [Test]
    public async Task DeleteByPredicate_Aggregate_SoftDelete()
    {
        // 条件删除对聚合重写为软删除：默认查询不再可见，物理行保留且标记 IsDeleted = 1
        var entity = new PlainTestEntity { Name = "条件软删除" };
        _ = await _plainRepository.InsertAsync(entity);
        var deleted = await _plainRepository.DeleteAsync(it => it.Id == entity.Id);

        var count = await _plainRepository.CountAsync();
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_plain_entity WHERE IsDeleted = 1"));
        // 按列更新绕过审计切面：仓储应兜底填充更新时间；无登录上下文时更新人保持 null（可空语义）
        var updatedAt = await _client.Ado.GetScalarAsync("SELECT UpdatedAt FROM it_plain_entity WHERE IsDeleted = 1");
        var updatedBy = await _client.Ado.GetScalarAsync("SELECT UpdatedBy FROM it_plain_entity WHERE IsDeleted = 1");
        // 版本令牌轮换：删除也推进版本，使旧令牌持有的实体更新失效（防墓碑复活）
        var version = await _client.Ado.GetScalarAsync("SELECT Version FROM it_plain_entity WHERE IsDeleted = 1");

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True, "条件删除应命中软删除行");
            Assert.That(count, Is.EqualTo(0), "软删除后默认查询不应可见");
            Assert.That(physicalCount, Is.EqualTo(1), "物理行应保留且标记 IsDeleted = 1");
            Assert.That(updatedAt, Is.Not.Null, "软删除应兜底填充更新时间");
            Assert.That(updatedBy is DBNull, Is.True, "无登录上下文时更新人应为 null");
            Assert.That(Convert.ToString(version), Is.Not.EqualTo(entity.Version.ToString()), "软删除应轮换版本令牌");
        });
    }

    [Test]
    public async Task SoftDelete_RotatesVersion_StaleEntityUpdateRejected()
    {
        // 防复活：软删除轮换版本令牌后，持删除前旧快照的实体更新（OptLock）应版本冲突被拒，
        // 否则旧快照的全字段 UPDATE（含 IsDeleted = false）会把墓碑复活
        var entity = new SoftDeleteTestEntity { Name = "防复活" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var stale = entity.Snapshot<SoftDeleteTestEntity>();

        await _softDeleteRepository.DeleteAsync(entity.Id);

        stale.Name = "旧令牌更新";
        var ex = Assert.ThrowsAsync<BusinessException>(() => _softDeleteRepository.UpdateAsync(stale));
        Assert.That(ex!.Message, Does.Contain("版本号"), "应以并发冲突拒绝旧令牌更新");

        // 墓碑未被复活：物理行仍保留且 IsDeleted = 1
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_soft_delete_entity WHERE IsDeleted = 1"));
        Assert.That(physicalCount, Is.EqualTo(1), "软删除标记不应被旧令牌更新翻转");
    }

    [Test]
    public async Task Restore_SoftDeletedEntity_BecomesVisibleAgain()
    {
        // 恢复软删除墓碑：恢复后默认查询重新可见
        var entity = new SoftDeleteTestEntity { Name = "将被恢复" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        await _softDeleteRepository.DeleteAsync(entity.Id);
        Assert.That(await _softDeleteRepository.CountAsync(), Is.EqualTo(0), "删除后默认不可见");

        var restored = await _softDeleteRepository.RestoreAsync(entity.Id);
        var countAfterRestore = await _softDeleteRepository.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True, "恢复应命中软删除行");
            Assert.That(countAfterRestore, Is.EqualTo(1), "恢复后应重新可见");
        });
        var reloaded = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        Assert.That(reloaded!.IsDeleted, Is.False);
    }

    [Test]
    public async Task Version_AutoUpdateMatrix_OnlyOptLockRotates()
    {
        // 版本特性边界实证：SugarColumn(IsEnableUpdateVersionValidation = true) 只在
        // ExecuteCommandWithOptLock（实体更新）路径自动轮换 Version；
        // 普通实体更新（ExecuteCommand）与按列更新（SetColumns）均不会自动维护该列——
        // 需要轮换的写入必须经仓储统一入口（UpdateAsync / ExecDeleteOrRestore 已兜底）
        var entity = new SoftDeleteTestEntity { Name = "版本矩阵" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var original = entity.Version;

        // 1. 普通实体更新（不走 OptLock）：不 WHERE 版本、不 SET 版本，令牌原地不动
        var plain = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        plain!.Name = "普通实体更新";
        await _client.Updateable(plain).ExecuteCommandAsync();
        var afterPlain = await GetVersionFromDbAsync(entity.Id);
        Assert.That(afterPlain, Is.EqualTo(original), "普通实体更新不应自动轮换 Version");

        // 2. 按列更新：同样不自动维护 Version
        await _client.Updateable<SoftDeleteTestEntity>()
            .SetColumns(it => it.Name == "按列更新")
            .Where(it => it.Id == entity.Id)
            .ExecuteCommandAsync();
        var afterSetColumns = await GetVersionFromDbAsync(entity.Id);
        Assert.That(afterSetColumns, Is.EqualTo(original), "按列更新不应自动轮换 Version");

        // 3. OptLock 实体更新：自动轮换（对照组，机制详见 InsertAndUpdate_FillsAuditFields）
        var latest = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        latest!.Name = "OptLock 更新";
        await _softDeleteRepository.UpdateAsync(latest);
        var afterOptLock = await GetVersionFromDbAsync(entity.Id);
        Assert.That(afterOptLock, Is.Not.EqualTo(original), "OptLock 实体更新应自动轮换 Version");
    }

    /// <summary>绕过仓储直查库中指定行的 Version（规避默认查询过滤器对墓碑行的屏蔽）。</summary>
    private async Task<Guid> GetVersionFromDbAsync(Guid id)
    {
        var value = await _client.Ado.GetScalarAsync(
            "SELECT Version FROM it_soft_delete_entity WHERE Id = @id",
            new { id });
        return Guid.Parse(Convert.ToString(value)!);
    }

    [Test]
    public async Task DeleteAndRestore_ByIdsAndPredicate_BatchSoftDelete()
    {
        // 批量软删除 / 恢复：ids 与 predicate 重载均基于单条 SQL 按列更新，返回是否命中（幂等重复执行为 false）
        var e1 = new SoftDeleteTestEntity { Name = "批量1" };
        _ = await _softDeleteRepository.InsertAsync(e1);
        var e2 = new SoftDeleteTestEntity { Name = "批量2" };
        _ = await _softDeleteRepository.InsertAsync(e2);

        var deleted = await _softDeleteRepository.DeleteAsync([e1.Id, e2.Id]);
        var countAfterDelete = await _softDeleteRepository.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True, "按主键集合删除应命中两行");
            Assert.That(countAfterDelete, Is.EqualTo(0), "删除后默认查询不可见");
        });

        // 重复删除同一批：墓碑行不再计入，幂等返回 0
        var deletedAgain = await _softDeleteRepository.DeleteAsync([e1.Id, e2.Id]);
        Assert.That(deletedAgain, Is.False, "已删除行不应重复计入（幂等返回 false）");

        // 按主键集合恢复：仅恢复第一行
        var restoredByIds = await _softDeleteRepository.RestoreAsync([e1.Id]);
        var countAfterRestoreByIds = await _softDeleteRepository.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restoredByIds, Is.True);
            Assert.That(countAfterRestoreByIds, Is.EqualTo(1));
        });

        // 按条件恢复剩余墓碑
        var restoredByPredicate = await _softDeleteRepository.RestoreAsync(it => it.Id == e2.Id);
        var countAfterRestoreAll = await _softDeleteRepository.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restoredByPredicate, Is.True);
            Assert.That(countAfterRestoreAll, Is.EqualTo(2), "全部恢复后应重新可见");
        });
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
