using System.Linq.Expressions;
using System.Reflection;
using Application.Commands;
using Application.Contracts.Commands;
using Application.Contracts.Notices;
using Application.Contracts.Queries;
using Application.Mappings;
using Application.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Services.Storage;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Microsoft.Extensions.Logging;

namespace Application.Tests.Commands;

/// <summary>
/// P2-9 回归单测：最新公告排序 / Take 下推数据库、角色菜单批量插入（去重 / 空集合边界）、
/// 文件上传元数据写入失败时的孤儿文件补偿删除。
/// </summary>
[TestFixture]
public class ApplicationHardeningTests
{
    /// <summary>构造与宿主 AddMaps 同口径的 AutoMapper 映射器。</summary>
    /// <returns>映射器。</returns>
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(CommonProfile).Assembly)).CreateMapper();

    /// <summary>从排序表达式中取出属性名（值类型字段表达式带 Convert 包装，需先解包）。</summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <param name="selector">排序字段表达式。</param>
    /// <returns>属性名。</returns>
    private static string MemberName<TEntity>(Expression<Func<TEntity, object>> selector)
    {
        var body = selector.Body is UnaryExpression unary ? unary.Operand : selector.Body;
        return ((MemberExpression)body).Member.Name;
    }

    /// <summary>构造乱序的公告集合：含停用项、相同创建时间的平局项（Id 较小者故意排在前面，用于验证 Id 平局裁决下推）。</summary>
    /// <returns>公告全量列表。</returns>
    private static List<SysNotice> BuildNotices()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = new Func<int, Guid>(i => new Guid($"00000000-0000-0000-0000-{i:D12}"));
        return
        [
            Notice(id(1), t.AddMinutes(5), CommonStatus.Enabled),
            Notice(id(4), t.AddMinutes(5), CommonStatus.Enabled),
            Notice(id(0), t.AddMinutes(7), CommonStatus.Disabled),
            Notice(id(3), t.AddMinutes(9), CommonStatus.Enabled),
            Notice(id(6), t.AddMinutes(1), CommonStatus.Enabled),
            Notice(id(5), t.AddMinutes(4), CommonStatus.Enabled),
            Notice(id(2), t.AddMinutes(3), CommonStatus.Enabled),
            Notice(id(7), t.AddMinutes(2), CommonStatus.Disabled)
        ];
    }

    /// <summary>构造单条公告。</summary>
    /// <param name="id">主键。</param>
    /// <param name="createdAt">创建时间。</param>
    /// <param name="status">状态。</param>
    /// <returns>公告实体。</returns>
    private static SysNotice Notice(Guid id, DateTime createdAt, CommonStatus status) => new()
    {
        Id = id,
        CreatedAt = createdAt,
        Status = status,
        Title = "公告" + id
    };

    /// <summary>最新公告走数据库侧分页（ORDER BY + LIMIT），保留 Enabled 过滤与创建时间 / Id 降序，结果与旧内存排序 + Take 一致。</summary>
    /// <param name="count">请求的返回条数。</param>
    /// <param name="expectedLimit">收敛后的条数。</param>
    /// <returns>异步任务。</returns>
    [TestCase(5, 5)]
    [TestCase(0, 5)]
    [TestCase(3, 3)]
    [TestCase(100, 20)]
    public async Task Latest_UsesDbPagedQuery_AndMatchesOldOrdering(int count, int expectedLimit)
    {
        var all = BuildNotices();
        var stub = new AggregateRepositoryStub<SysNotice> { All = all };
        var handler = new NoticeQueryLatestHandler(stub, CreateMapper());

        var result = await handler.Handle(new NoticeQueryLatest(count), CancellationToken.None);

        // 旧逻辑：全量 → 过滤 → 创建时间倒序、Id 倒序 → Take
        var expectedIds = all
            .Where(n => n.Status == CommonStatus.Enabled)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(expectedLimit)
            .Select(n => n.Id)
            .ToList();
        var orderEntries = stub.LastOrders!
            .Select(kv => (Name: MemberName<SysNotice>(kv.Key), Ascending: kv.Value))
            .ToList();
        var predicate = stub.LastPredicates[0].Compile();

        Assert.Multiple(() =>
        {
            Assert.That(stub.GetListAsyncCalled, Is.False, "不应再拉全表");
            Assert.That(stub.LastPageRequest, Is.Not.Null, "应走 GetPagedAsync 下推排序与取数");
            Assert.That(stub.LastPageRequest!.PageIndex, Is.EqualTo(1));
            Assert.That(stub.LastPageRequest.PageSize, Is.EqualTo(expectedLimit), "页大小即收敛后的 Take 条数");
            Assert.That(stub.LastOrders, Is.Not.Null);
            Assert.That(orderEntries.Select(e => e.Name), Is.EquivalentTo(new[] { "CreatedAt", "Id" }));
            Assert.That(orderEntries.All(e => e.Ascending == false), Is.True, "两列均应为降序");
            Assert.That(stub.LastPredicates, Has.Count.EqualTo(1));
            Assert.That(predicate(new SysNotice { Status = CommonStatus.Enabled }), Is.True, "保留 Enabled 过滤");
            Assert.That(predicate(new SysNotice { Status = CommonStatus.Disabled }), Is.False);
            Assert.That(result.Select(r => r.Id), Is.EqualTo(expectedIds), "映射结果与旧内存排序 + Take 完全一致");
        });
    }

    /// <summary>反射调用 internal RoleMenuSupport.ReplaceRoleMenusAsync（测试项目无 InternalsVisibleTo，直接精确覆盖该方法边界）。</summary>
    /// <param name="repository">角色菜单关联仓储桩。</param>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="menuIds">菜单 Id 集合。</param>
    /// <returns>异步任务。</returns>
    private static Task ReplaceRoleMenusAsync(IDomainRepository<SysRoleMenu> repository, Guid roleId, List<Guid> menuIds)
    {
        var supportType = typeof(RoleCreateCommandHandler).Assembly.GetType("Application.Commands.RoleMenuSupport")
            ?? throw new InvalidOperationException("未找到 RoleMenuSupport 类型");
        var method = supportType.GetMethod("ReplaceRoleMenusAsync", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到 ReplaceRoleMenusAsync 方法");
        return (Task)method.Invoke(null, [repository, roleId, menuIds, CancellationToken.None])!;
    }

    /// <summary>全量替换走批量插入重载，且对重复菜单 Id 去重。</summary>
    /// <returns>异步任务。</returns>
    [Test]
    public async Task ReplaceRoleMenus_Deduplicates_AndUsesBatchInsert()
    {
        var roleId = Guid.NewGuid();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var m3 = Guid.NewGuid();
        var stub = new DomainRepositoryStub<SysRoleMenu>();

        await ReplaceRoleMenusAsync(stub, roleId, [m1, m2, m2, m3, m1]);

        var batch = stub.BatchInsertCalls[0];
        Assert.Multiple(() =>
        {
            Assert.That(stub.HardDeleteCount, Is.EqualTo(1), "先按角色物理删除旧授权");
            Assert.That(stub.SingleInsertCount, Is.Zero, "不应再逐条 InsertAsync");
            Assert.That(stub.BatchInsertCalls, Has.Count.EqualTo(1), "应调用批量插入重载");
            Assert.That(batch, Has.Count.EqualTo(3), "重复项应去重");
            Assert.That(batch.Select(item => item.MenuId), Is.EquivalentTo(new[] { m1, m2, m3 }));
            Assert.That(batch.All(item => item.RoleId == roleId), Is.True);
        });
    }

    /// <summary>空菜单集合仍执行一次旧授权删除，批量插入收到空集合（实现内部短路，不抛异常）。</summary>
    /// <returns>异步任务。</returns>
    [Test]
    public async Task ReplaceRoleMenus_EmptyCollection_StillDeletes_AndSkipsInserts()
    {
        var stub = new DomainRepositoryStub<SysRoleMenu>();

        await ReplaceRoleMenusAsync(stub, Guid.NewGuid(), []);

        Assert.Multiple(() =>
        {
            Assert.That(stub.HardDeleteCount, Is.EqualTo(1));
            Assert.That(stub.SingleInsertCount, Is.Zero);
            Assert.That(stub.BatchInsertCalls, Has.Count.EqualTo(1), "空集合仍走批量 API（实现内部短路）");
            Assert.That(stub.BatchInsertCalls[0], Is.Empty);
        });
    }

    /// <summary>元数据写入失败时补偿删除物理文件，业务异常照常向外传播，成功路径不发布事件。</summary>
    [Test]
    public void Upload_MetadataFailure_DeletesOrphan_AndRethrows()
    {
        var storage = new FileStorageStub();
        var repository = new AggregateRepositoryStub<SysFile> { InsertException = new InvalidOperationException("元数据写入失败") };
        var publisher = new DomainEventPublisherStub();
        var logger = new CapturingLogger<FileUploadCommandHandler>();
        var handler = new FileUploadCommandHandler(repository, storage, publisher, logger);
        var command = new FileUploadCommand("a.png", "image/png", null, new MemoryStream([1, 2, 3]));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await handler.Handle(command, CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("元数据写入失败"), "业务异常原样传播");
            Assert.That(storage.SaveCount, Is.EqualTo(1));
            Assert.That(storage.DeletedPaths, Is.EqualTo(new[] { "2026/11/stored.bin" }), "元数据失败时补偿删除物理文件");
            Assert.That(publisher.PublishCount, Is.Zero, "失败路径不发布领域事件");
            Assert.That(logger.Entries, Is.Empty, "删除成功时不告警");
        });
    }

    /// <summary>补偿删除本身失败时吞掉异常并记 Warning，不得顶替或掩盖业务异常。</summary>
    [Test]
    public void Upload_MetadataFailure_DeleteFailure_IsSwallowed_AndWarns()
    {
        var storage = new FileStorageStub { ThrowOnDelete = true };
        var repository = new AggregateRepositoryStub<SysFile> { InsertException = new InvalidOperationException("元数据写入失败") };
        var publisher = new DomainEventPublisherStub();
        var logger = new CapturingLogger<FileUploadCommandHandler>();
        var handler = new FileUploadCommandHandler(repository, storage, publisher, logger);
        var command = new FileUploadCommand("a.png", "image/png", null, new MemoryStream([1]));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await handler.Handle(command, CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("元数据写入失败"), "业务异常不被删除异常顶替");
            Assert.That(storage.DeletedPaths, Has.Count.EqualTo(1));
            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(logger.Entries[0].Message, Does.Contain("2026/11/stored.bin"));
            Assert.That(publisher.PublishCount, Is.Zero);
        });
    }

    /// <summary>上传成功路径不误删物理文件，正常发布事件。</summary>
    /// <returns>异步任务。</returns>
    [Test]
    public async Task Upload_Success_DoesNotDeletePhysicalFile()
    {
        var storage = new FileStorageStub();
        var repository = new AggregateRepositoryStub<SysFile>();
        var publisher = new DomainEventPublisherStub();
        var logger = new CapturingLogger<FileUploadCommandHandler>();
        var handler = new FileUploadCommandHandler(repository, storage, publisher, logger);
        var command = new FileUploadCommand("a.png", "image/png", null, new MemoryStream([1, 2, 3]));

        var response = await handler.Handle(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.FileName, Is.EqualTo("a.png"));
            Assert.That(response.SizeBytes, Is.EqualTo(8));
            Assert.That(storage.DeletedPaths, Is.Empty, "成功路径不得删除物理文件");
            Assert.That(publisher.PublishCount, Is.EqualTo(1));
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    /// <summary>领域仓储记录桩：记录分页参数、逐条 / 批量插入与物理删除调用，并按传入条件与排序模拟数据库侧行为。</summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    private class DomainRepositoryStub<TEntity> : IDomainRepository<TEntity>
        where TEntity : DomainBase
    {
        /// <summary>参与查询的全量实体（模拟表数据）。</summary>
        public List<TEntity> All { get; init; } = [];

        /// <summary>单条插入时抛出的异常（模拟元数据写入失败）；null 表示插入成功。</summary>
        public Exception? InsertException { get; init; }

        /// <summary>是否调用过列表查询（用于断言不再拉全表）。</summary>
        public bool GetListAsyncCalled { get; private set; }

        /// <summary>最近一次分页请求参数。</summary>
        public PagedRequest? LastPageRequest { get; private set; }

        /// <summary>最近一次分页排序字典。</summary>
        public IDictionary<Expression<Func<TEntity, object>>, bool?>? LastOrders { get; private set; }

        /// <summary>最近一次分页过滤条件。</summary>
        public List<Expression<Func<TEntity, bool>>> LastPredicates { get; } = [];

        /// <summary>逐条插入调用次数。</summary>
        public int SingleInsertCount { get; private set; }

        /// <summary>每次批量插入收到的实体集合。</summary>
        public List<List<TEntity>> BatchInsertCalls { get; } = [];

        /// <summary>物理删除调用次数。</summary>
        public int HardDeleteCount { get; private set; }

        /// <inheritdoc />
        public Task<TEntity?> GetAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult<TEntity?>(null);

        /// <inheritdoc />
        public Task<TDto?> GetAsync<TDto>(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            where TDto : class, new()
            => Task.FromResult<TDto?>(null);

        /// <inheritdoc />
        public Task<List<TEntity>> GetListAsync(
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
        {
            GetListAsyncCalled = true;
            return Task.FromResult(new List<TEntity>(All));
        }

        /// <inheritdoc />
        public Task<List<TDto>> GetListAsync<TDto>(
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => Task.FromResult<List<TDto>>([]);

        /// <inheritdoc />
        public Task<PagedResponse<TEntity>> GetPagedAsync(
            PagedRequest request,
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
        {
            LastPageRequest = request;
            LastOrders = orders;
            LastPredicates.Clear();
            LastPredicates.AddRange(predicates?.Where(p => p is not null) ?? []);

            IEnumerable<TEntity> query = All;
            foreach (var predicate in LastPredicates)
            {
                query = query.Where(predicate.Compile());
            }

            if (orders is { Count: > 0 })
            {
                IOrderedEnumerable<TEntity>? ordered = null;
                foreach (var (selector, ascending) in orders)
                {
                    if (selector is null)
                    {
                        continue;
                    }

                    var key = selector.Compile();
                    ordered = ordered is null
                        ? (ascending == true
                            ? query.OrderBy(key, Comparer<object>.Default)
                            : query.OrderByDescending(key, Comparer<object>.Default))
                        : (ascending == true
                            ? ordered.ThenBy(key, Comparer<object>.Default)
                            : ordered.ThenByDescending(key, Comparer<object>.Default));
                }

                if (ordered is not null)
                {
                    query = ordered;
                }
            }

            var items = query.Take(request.PageSize).ToList();
            return Task.FromResult(PagedResponse<TEntity>.Create(items, query.Count(), request));
        }

        /// <inheritdoc />
        public Task<PagedResponse<TDto>> GetPagedAsync<TDto>(
            PagedRequest request,
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> AnyAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult(All.Count > 0);

        /// <inheritdoc />
        public Task<long> CountAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult((long)All.Count);

        /// <inheritdoc />
        public Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            SingleInsertCount++;
            return InsertException is not null
                ? Task.FromException<bool>(InsertException)
                : Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var materialized = entities as List<TEntity> ?? [.. entities];
            BatchInsertCalls.Add(materialized);
            return Task.FromResult(materialized.Count > 0);
        }

        /// <inheritdoc />
        public Task<bool> HardDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            HardDeleteCount++;
            return Task.FromResult(true);
        }
    }

    /// <summary>聚合仓储记录桩：在领域仓储桩之上补齐聚合根专属能力（本组用例均不触发，统一抛 NotSupportedException）。</summary>
    /// <typeparam name="TEntity">聚合实体类型。</typeparam>
    private sealed class AggregateRepositoryStub<TEntity> : DomainRepositoryStub<TEntity>, IAggregateRepository<TEntity>
        where TEntity : AggregateBase
    {
        /// <inheritdoc />
        public Task<TEntity?> GetDeletedAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null) => throw new NotSupportedException();
    }

    /// <summary>文件存储桩：记录保存 / 删除调用，可配置删除时抛 IO 异常。</summary>
    private sealed class FileStorageStub : IFileStorage
    {
        /// <summary>已请求删除的相对路径。</summary>
        public List<string> DeletedPaths { get; } = [];

        /// <summary>删除时是否抛出 IO 异常。</summary>
        public bool ThrowOnDelete { get; init; }

        /// <summary>保存调用次数。</summary>
        public int SaveCount { get; private set; }

        /// <inheritdoc />
        public Task<FileSaveResult> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(new FileSaveResult("stored.bin", "2026/11/stored.bin", "application/octet-stream", 8));
        }

        /// <inheritdoc />
        public Stream OpenRead(string relativePath) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Delete(string relativePath)
        {
            DeletedPaths.Add(relativePath);
            if (ThrowOnDelete)
            {
                throw new IOException("模拟磁盘 IO 故障");
            }
        }
    }

    /// <summary>领域事件发布桩：仅记录发布次数。</summary>
    private sealed class DomainEventPublisherStub : IDomainEventPublisher
    {
        /// <summary>发布次数。</summary>
        public int PublishCount { get; private set; }

        /// <inheritdoc />
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : DomainEvent
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>记录日志级别与消息的假日志器。</summary>
    /// <typeparam name="TCategory">日志类别。</typeparam>
    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>已记录的日志条目。</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
