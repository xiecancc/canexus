
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
/// <para>连接串强制经环境变量 SUPER_ADMIN_TEST_CONN 注入（P1-R8：移除内置密码回退）。</para>
/// </summary>
[TestFixture]
public class DataAccessIntegrationTests
{
    private ServiceProvider _provider = null!;
    private ISqlSugarClient _client = null!;
    private IAggregateRepository<SoftDeleteTestEntity> _softDeleteRepository = null!;
    private IAggregateRepository<MultiTenantTestEntity> _tenantRepository = null!;
    private IAggregateRepository<PlainTestEntity> _plainRepository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ITenantContext _tenantContext = null!;
    private IDataFilter _dataFilter = null!;

    /// <summary>一次性初始化：按生产口径装配数据访问并建库建表。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = TestDatabaseSupport.TestConnectionString;

        // 复用生产注册入口（IConfiguration 重载），内存配置覆盖测试连接串
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connectionString,
                // 迁移机制由 SchemaMigrationTests 在独立临时库验证；本夹具带测试实体（表集与共享库不同），
                // 关闭迁移块，避免测试实体的结构参与共享库初始化判定
                ["Database:MigrationsEnabled"] = "false"
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

        // 事务语义要求 InnoDB：本夹具以 MigrationsEnabled=false 走「跳过版本判定」分支，
        // 不会执行生产初始化路径的引擎修正；而部分 MySQL 实例默认引擎为 MyISAM（不支持事务），
        // 会让 UnitOfWork 回滚用例误判。按 information_schema 逐表幂等修正，保证全新库亦可通过。
        foreach (var tableName in new[] { "it_soft_delete_entity", "it_multi_tenant_entity", "it_plain_entity" })
        {
            var engine = await _client.Ado.GetScalarAsync(
                "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName",
                new SugarParameter("@tableName", tableName));
            if (!string.Equals(engine?.ToString(), "InnoDB", StringComparison.OrdinalIgnoreCase))
            {
                await _client.Ado.ExecuteCommandAsync($"ALTER TABLE `{tableName}` ENGINE = InnoDB");
            }
        }
    }

    /// <summary>一次性清理：释放客户端与服务提供程序。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _provider.Dispose();
    }

    /// <summary>每个测试前清空数据并重置租户与过滤上下文，保证测试独立。</summary>
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

    /// <summary>验证 CodeFirst 初始化后三张测试表均已创建。</summary>
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

    /// <summary>验证工作单元事务内异常时自动回滚，插入数据被丢弃。</summary>
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

    /// <summary>验证工作单元事务正常结束时提交，插入数据持久化。</summary>
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

    /// <summary>验证插入与更新时审计字段自动填充且版本令牌轮换。</summary>
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
            Assert.That(entity.DeletedAt, Is.Null, "插入时软删除时间戳应为 null");
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

    /// <summary>验证持过期版本令牌提交更新时抛出并发冲突异常。</summary>
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

    /// <summary>验证软删除后默认查询不可见但物理行保留并标记已删除。</summary>
    [Test]
    public async Task SoftDelete_MarkedDeleted_InvisibleByDefault()
    {
        // 软删除：删除后默认查询不可见，但物理行仍存在且标记为已删除
        var entity = new SoftDeleteTestEntity { Name = "将被删除" };
        _ = await _softDeleteRepository.InsertAsync(entity);

        await _softDeleteRepository.SoftDeleteAsync(entity.Id);

        var fetched = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        var count = await _softDeleteRepository.CountAsync();
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_soft_delete_entity WHERE DeletedAt IS NOT NULL"));

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.Null, "软删除后默认查询不应可见");
            Assert.That(count, Is.EqualTo(0), "软删除后计数应为 0");
            Assert.That(physicalCount, Is.EqualTo(1), "物理行应保留且DeletedAt 非空");
        });
    }

    /// <summary>验证关闭软删除过滤开关后已删除数据恢复可见。</summary>
    [Test]
    public async Task SoftDelete_DisabledFilter_MakesDeletedVisible()
    {
        // 关闭软删除过滤开关后，已删除数据恢复可见
        var entity = new SoftDeleteTestEntity { Name = "过滤开关" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        await _softDeleteRepository.SoftDeleteAsync(entity.Id);

        _dataFilter.SoftDeleteEnabled = false;
        var count = await _softDeleteRepository.CountAsync();

        Assert.That(count, Is.EqualTo(1), "关闭过滤开关后已删除数据应可见");
    }

    /// <summary>验证按条件软删除聚合时兜底填充审计与版本。</summary>
    [Test]
    public async Task DeleteByPredicate_Aggregate_SoftDelete()
    {
        // 条件软删除：默认查询不再可见，物理行保留且DeletedAt 非空
        var entity = new PlainTestEntity { Name = "条件软删除" };
        _ = await _plainRepository.InsertAsync(entity);
        var deleted = await _plainRepository.SoftDeleteAsync(it => it.Id == entity.Id);

        var count = await _plainRepository.CountAsync();
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_plain_entity WHERE DeletedAt IS NOT NULL"));
        // 按列更新绕过审计切面：仓储应兜底填充更新时间；无登录上下文时更新人保持 null（可空语义）
        var updatedAt = await _client.Ado.GetScalarAsync("SELECT UpdatedAt FROM it_plain_entity WHERE DeletedAt IS NOT NULL");
        var updatedBy = await _client.Ado.GetScalarAsync("SELECT UpdatedBy FROM it_plain_entity WHERE DeletedAt IS NOT NULL");
        // 版本令牌轮换：删除也推进版本，使旧令牌持有的实体更新失效（防墓碑复活）
        var version = await _client.Ado.GetScalarAsync("SELECT Version FROM it_plain_entity WHERE DeletedAt IS NOT NULL");

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True, "条件删除应命中软删除行");
            Assert.That(count, Is.EqualTo(0), "软删除后默认查询不应可见");
            Assert.That(physicalCount, Is.EqualTo(1), "物理行应保留且DeletedAt 非空");
            Assert.That(updatedAt, Is.Not.Null, "软删除应兜底填充更新时间");
            Assert.That(updatedBy is DBNull, Is.True, "无登录上下文时更新人应为 null");
            Assert.That(Convert.ToString(version), Is.Not.EqualTo(entity.Version.ToString()), "软删除应轮换版本令牌");
        });
    }

    /// <summary>验证软删除轮换版本令牌后旧快照实体的更新被并发冲突拒绝。</summary>
    [Test]
    public async Task SoftDelete_RotatesVersion_StaleEntityUpdateRejected()
    {
        // 防复活：软删除轮换版本令牌后，持删除前旧快照的实体更新（OptLock）应版本冲突被拒，
        // 否则旧快照的全字段 UPDATE（含 DeletedAt = null）会把墓碑复活
        var entity = new SoftDeleteTestEntity { Name = "防复活" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var stale = entity.Snapshot<SoftDeleteTestEntity>();

        await _softDeleteRepository.SoftDeleteAsync(entity.Id);

        stale.Name = "旧令牌更新";
        var ex = Assert.ThrowsAsync<BusinessException>(() => _softDeleteRepository.UpdateAsync(stale));
        Assert.That(ex!.Message, Does.Contain("版本号"), "应以并发冲突拒绝旧令牌更新");

        // 墓碑未被复活：物理行仍保留且 DeletedAt 非空
        var physicalCount = Convert.ToInt32(
            await _client.Ado.GetScalarAsync("SELECT COUNT(*) FROM it_soft_delete_entity WHERE DeletedAt IS NOT NULL"));
        Assert.That(physicalCount, Is.EqualTo(1), "软删除时间戳不应被旧令牌更新翻转");
    }

    /// <summary>验证软删除实体恢复后默认查询重新可见。</summary>
    [Test]
    public async Task Restore_SoftDeletedEntity_BecomesVisibleAgain()
    {
        // 恢复软删除墓碑：恢复后默认查询重新可见
        var entity = new SoftDeleteTestEntity { Name = "将被恢复" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        await _softDeleteRepository.SoftDeleteAsync(entity.Id);
        Assert.That(await _softDeleteRepository.CountAsync(), Is.EqualTo(0), "删除后默认不可见");

        var restored = await _softDeleteRepository.RestoreAsync(entity.Id);
        var countAfterRestore = await _softDeleteRepository.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True, "恢复应命中软删除行");
            Assert.That(countAfterRestore, Is.EqualTo(1), "恢复后应重新可见");
        });
        var reloaded = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        Assert.That(reloaded!.DeletedAt, Is.Null);
    }

    /// <summary>软删除写入 DeletedAt 时间戳并轮换版本令牌；恢复置回 null 并再次轮换（三条路径统一 OptLock）。</summary>
    [Test]
    public async Task SoftDeleteAndRestore_WriteDeletedAtTimestamp_AndRotateVersion()
    {
        var entity = new SoftDeleteTestEntity { Name = "时间戳墓碑" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var versionOnInsert = entity.Version;

        Assert.That(await _softDeleteRepository.SoftDeleteAsync(entity.Id), Is.True);
        var tombstone = await _softDeleteRepository.GetDeletedAsync(entity.Id);
        Assert.That(tombstone, Is.Not.Null, "软删除后应能按主键读到墓碑");
        var versionAfterDelete = await GetVersionFromDbAsync(entity.Id);
        Assert.Multiple(() =>
        {
            Assert.That(tombstone!.DeletedAt, Is.Not.Null, "软删除应写入 DeletedAt 时间戳");
            Assert.That(tombstone.UpdatedAt, Is.Not.Null, "软删除应兜底填充更新时间");
            Assert.That(versionAfterDelete, Is.Not.EqualTo(versionOnInsert), "软删除应经 OptLock 轮换版本令牌");
        });

        Assert.That(await _softDeleteRepository.RestoreAsync(entity.Id), Is.True);
        var restored = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        var versionAfterRestore = await GetVersionFromDbAsync(entity.Id);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.DeletedAt, Is.Null, "恢复应把 DeletedAt 置回 null");
            Assert.That(versionAfterRestore, Is.Not.EqualTo(versionAfterDelete), "恢复应经 OptLock 再次轮换版本令牌");
        });
    }

    /// <summary>软删除携带陈旧版本令牌时抛并发冲突，且不得留下墓碑。</summary>
    [Test]
    public async Task SoftDelete_StaleVersion_ThrowsConcurrencyConflict_AndLeavesRowAlive()
    {
        var entity = new SoftDeleteTestEntity { Name = "陈旧令牌软删除" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var staleVersion = entity.Version;

        // 另一个会话先更新一次，使库中版本令牌轮换
        var latest = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        latest!.Name = "他人已更新";
        await _softDeleteRepository.UpdateAsync(latest);

        var exception = Assert.ThrowsAsync<BusinessException>(
            () => _softDeleteRepository.SoftDeleteAsync(entity.Id, default, staleVersion));
        Assert.That(exception!.Message, Does.Contain("版本号"), "应以并发冲突拒绝陈旧令牌");

        var alive = await _softDeleteRepository.GetAsync([it => it.Id == entity.Id]);
        var tombstone = await _softDeleteRepository.GetDeletedAsync(entity.Id);
        Assert.Multiple(() =>
        {
            Assert.That(alive, Is.Not.Null, "陈旧令牌的软删除必须被拒绝，行保持可见");
            Assert.That(tombstone, Is.Null, "陈旧令牌的软删除不得留下墓碑");
        });
    }

    /// <summary>恢复携带陈旧版本令牌时抛并发冲突，墓碑保持不被解除。</summary>
    [Test]
    public async Task Restore_StaleVersion_ThrowsConcurrencyConflict_AndKeepsTombstone()
    {
        var entity = new SoftDeleteTestEntity { Name = "陈旧令牌恢复" };
        _ = await _softDeleteRepository.InsertAsync(entity);
        var versionOnInsert = entity.Version;
        Assert.That(await _softDeleteRepository.SoftDeleteAsync(entity.Id, default, versionOnInsert), Is.True);

        // 软删除已轮换版本令牌，插入时的令牌即为陈旧令牌
        var exception = Assert.ThrowsAsync<BusinessException>(
            () => _softDeleteRepository.RestoreAsync(entity.Id, default, versionOnInsert));
        Assert.That(exception!.Message, Does.Contain("版本号"), "应以并发冲突拒绝陈旧令牌");

        var tombstone = await _softDeleteRepository.GetDeletedAsync(entity.Id);
        Assert.That(tombstone, Is.Not.Null, "陈旧令牌的恢复必须被拒绝，墓碑保持");
        Assert.That(tombstone!.DeletedAt, Is.Not.Null);
    }

    /// <summary>验证版本令牌仅在 OptLock 实体更新路径自动轮换，普通与按列更新均不维护。</summary>
    [Test]
    public async Task Version_AutoUpdateMatrix_OnlyOptLockRotates()
    {
        // 版本特性边界实证：SugarColumn(IsEnableUpdateVersionValidation = true) 只在
        // ExecuteCommandWithOptLock（实体更新）路径自动轮换 Version；
        // 普通实体更新（ExecuteCommand）与按列更新（SetColumns）均不会自动维护该列——
        // 需要轮换的写入必须经仓储统一入口（UpdateAsync / ExecDeleteOrRestoreAsync 已兜底）
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

    /// <summary>验证按主键集合与条件谓词的批量软删除及恢复均幂等正确。</summary>
    [Test]
    public async Task DeleteAndRestore_ByIdsAndPredicate_BatchSoftDelete()
    {
        // 批量软删除 / 恢复：ids 与 predicate 重载逐行经 OptLock 按列更新，返回是否命中（幂等重复执行为 false）
        var e1 = new SoftDeleteTestEntity { Name = "批量1" };
        _ = await _softDeleteRepository.InsertAsync(e1);
        var e2 = new SoftDeleteTestEntity { Name = "批量2" };
        _ = await _softDeleteRepository.InsertAsync(e2);

        var deleted = await _softDeleteRepository.SoftDeleteAsync([e1.Id, e2.Id]);
        var countAfterDelete = await _softDeleteRepository.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True, "按主键集合删除应命中两行");
            Assert.That(countAfterDelete, Is.EqualTo(0), "删除后默认查询不可见");
        });

        // 重复删除同一批：墓碑行不再计入，幂等返回 0
        var deletedAgain = await _softDeleteRepository.SoftDeleteAsync([e1.Id, e2.Id]);
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

    /// <summary>验证租户过滤钩子按当前租户隔离数据可见性。</summary>
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

    /// <summary>
    /// P1-2 口径统一：平台用户（CurrentUser.TenantId 为 null，集成测试无 HTTP 上下文即如此）通过
    /// X-Tenant-Id 指定 tenantContext.TenantId 时，GetDeletedAsync 必须按该请求租户约束，
    /// 不得因登录用户无租户声明而跨租户读取墓碑（与 LoadTargetsAsync 统一取 CurrentTenantId）。
    /// </summary>
    [Test]
    public async Task GetDeletedAsync_UsesRequestTenantContext_ForPlatformUserWithTenantHeader()
    {
        var tenantA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var tenantB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

        // 平台态（tenantContext 为 null）下插入两条分属不同租户的实体，再分别软删除为墓碑
        var entityA = new MultiTenantTestEntity { Name = "A-墓碑", TenantId = tenantA };
        var entityB = new MultiTenantTestEntity { Name = "B-墓碑", TenantId = tenantB };
        await _tenantRepository.InsertAsync(entityA);
        await _tenantRepository.InsertAsync(entityB);
        Assert.That(await _tenantRepository.SoftDeleteAsync(entityA.Id), Is.True);
        Assert.That(await _tenantRepository.SoftDeleteAsync(entityB.Id), Is.True);

        // 平台用户带 X-Tenant-Id：请求租户上下文收敛到 tenantA（等价于中间件写入 tenantContext）
        _tenantContext.TenantId = tenantA;

        var ownTombstone = await _tenantRepository.GetDeletedAsync(entityA.Id);
        var crossTenantTombstone = await _tenantRepository.GetDeletedAsync(entityB.Id);

        Assert.Multiple(() =>
        {
            Assert.That(ownTombstone, Is.Not.Null, "本租户墓碑应可读");
            Assert.That(crossTenantTombstone, Is.Null,
                "平台用户带 X-Tenant-Id 时 GetDeletedAsync 不得跨租户读墓碑（须按 CurrentTenantId 而非 CurrentUser.TenantId）");
        });
    }

    /// <summary>验证分页查询返回正确的页数据与总数。</summary>
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
