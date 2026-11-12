using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Dtos.Common;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Tests.Commands;

/// <summary>
/// P1-A2 回归单测：聚合创建 / 更新命令基类的 AfterCreateAsync / AfterUpdateAsync 虚钩子
/// 在「仓储写入成功之后、领域事件发布之前」按序执行，创建钩子接收终态聚合（主键已填充），
/// 更新钩子接收变更前快照与变更后聚合；默认实现为空操作时标准流程不受影响。
/// </summary>
[TestFixture]
public class CommandHandlerHookTests
{
    /// <summary>测试载荷：仅用于满足 AggregateInputRequest 约束，不携带业务字段。</summary>
    private sealed class TestInputRequest : AggregateInputRequest;

    /// <summary>测试创建命令。</summary>
    /// <param name="request">创建载荷。</param>
    private sealed record TestCreateCommand(TestInputRequest request)
        : AggregateCreateCommand<TestInputRequest>(request);

    /// <summary>测试更新命令。</summary>
    /// <param name="id">目标聚合 id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新载荷。</param>
    private sealed record TestUpdateCommand(Guid id, Guid version, TestInputRequest request)
        : AggregateUpdateCommand<TestInputRequest>(id, version, request);

    /// <summary>测试领域事件：携带公告聚合。</summary>
    /// <param name="entity">事件关联实体。</param>
    private sealed class TestNoticeEvent(SysNotice entity) : DomainEvent<SysNotice>(entity);

    /// <summary>创建钩子按「validate → map → insert → after-create → publish」的固定次序执行，且钩子收到与插入同一实例。</summary>
    /// <returns>异步任务。</returns>
    [Test]
    public async Task CreateHandler_AfterCreateHook_RunsBetweenInsertAndPublish()
    {
        var steps = new List<string>();
        var repository = new RecordingRepository(steps);
        var publisher = new RecordingPublisher(steps);
        var handler = new TestCreateHandler(repository, publisher, steps);

        var result = await handler.Handle(new TestCreateCommand(new TestInputRequest()), CancellationToken.None);

        Assert.That(result, Is.True);
        Assert.That(steps, Is.EqualTo(["create:validate", "create:map", "insert", "create:after", "publish"]));
        Assert.That(handler.HookEntity, Is.SameAs(repository.LastInserted));
        Assert.That(handler.HookEntity, Is.Not.Null);
    }

    /// <summary>更新钩子在乐观锁更新之后、事件发布之前执行，接收变更前快照与变更后同一聚合实例。</summary>
    /// <returns>异步任务。</returns>
    [Test]
    public async Task UpdateHandler_AfterUpdateHook_RunsBetweenUpdateAndPublish()
    {
        var steps = new List<string>();
        var entity = new SysNotice { Id = Guid.NewGuid(), Version = Guid.NewGuid(), Title = "旧标题" };
        var repository = new RecordingRepository(steps, entity);
        var publisher = new RecordingPublisher(steps);
        var handler = new TestUpdateHandler(repository, publisher, steps);

        var result = await handler.Handle(
            new TestUpdateCommand(entity.Id, entity.Version, new TestInputRequest()), CancellationToken.None);

        Assert.That(result, Is.True);
        Assert.That(
            steps,
            Is.EqualTo(["get", "update:validate", "update:map", "update", "update:after", "publish"]));
        Assert.That(handler.HookAfter, Is.SameAs(entity));
        Assert.That(handler.HookAfter, Is.Not.SameAs(handler.HookBefore));
        Assert.That(handler.HookAfter!.Title, Is.EqualTo("新标题"));
        Assert.That(handler.HookBefore!.Title, Is.EqualTo("旧标题"));
    }

    /// <summary>创建处理器桩：全部钩子实现记录调用次序，创建后钩子捕获收到的聚合。</summary>
    private sealed class TestCreateHandler(
        RecordingRepository repository,
        RecordingPublisher publisher,
        List<string> steps)
        : AggregateCreateCommandHandler<TestCreateCommand, TestInputRequest, SysNotice>(repository, publisher)
    {
        /// <summary>创建后钩子收到的聚合。</summary>
        public SysNotice? HookEntity { get; private set; }

        /// <inheritdoc />
        protected override Task ValidateAsync(TestInputRequest request, CancellationToken cancellationToken)
        {
            steps.Add("create:validate");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override SysNotice MapToEntity(TestInputRequest request)
        {
            steps.Add("create:map");
            return new SysNotice { Title = "新公告" };
        }

        /// <inheritdoc />
        protected override DomainEvent CreateEvent(SysNotice inserted) => new TestNoticeEvent(inserted);

        /// <inheritdoc />
        protected override Task AfterCreateAsync(SysNotice entity, CancellationToken cancellationToken)
        {
            steps.Add("create:after");
            HookEntity = entity;
            return Task.CompletedTask;
        }
    }

    /// <summary>更新处理器桩：更新后钩子捕获变更前快照与变更后聚合。</summary>
    private sealed class TestUpdateHandler(
        RecordingRepository repository,
        RecordingPublisher publisher,
        List<string> steps)
        : AggregateUpdateCommandHandler<TestUpdateCommand, TestInputRequest, SysNotice>(repository, publisher)
    {
        /// <inheritdoc />
        protected override int NotFoundErrorCode => ErrorCodes.CommonDataNotFound;

        /// <inheritdoc />
        protected override string EntityDisplayName => "公告";

        /// <summary>更新后钩子收到的变更前快照。</summary>
        public SysNotice? HookBefore { get; private set; }

        /// <summary>更新后钩子收到的变更后聚合。</summary>
        public SysNotice? HookAfter { get; private set; }

        /// <inheritdoc />
        protected override Task ValidateAsync(TestInputRequest request, SysNotice entity, CancellationToken cancellationToken)
        {
            steps.Add("update:validate");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void MapToEntity(TestInputRequest request, SysNotice entity)
        {
            steps.Add("update:map");
            entity.Title = "新标题";
        }

        /// <inheritdoc />
        protected override DomainEvent CreateEvent(SysNotice before, SysNotice after) => new TestNoticeEvent(after);

        /// <inheritdoc />
        protected override Task AfterUpdateAsync(SysNotice before, SysNotice entity, CancellationToken cancellationToken)
        {
            steps.Add("update:after");
            HookBefore = before;
            HookAfter = entity;
            return Task.CompletedTask;
        }
    }

    /// <summary>聚合仓储记录桩：预置单条聚合，仅实现创建 / 更新流程触达的成员并记录调用次序。</summary>
    private sealed class RecordingRepository(List<string> steps, SysNotice? seed = null) : IAggregateRepository<SysNotice>
    {
        /// <summary>最近一次插入的聚合。</summary>
        public SysNotice? LastInserted { get; private set; }

        /// <inheritdoc />
        public Task<SysNotice?> GetAsync(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            CancellationToken cancellationToken = default)
        {
            steps.Add("get");
            var match = seed;
            if (match is not null && predicates is not null && !predicates.All(p => p.Compile()(match)))
            {
                match = null;
            }

            return Task.FromResult(match);
        }

        /// <inheritdoc />
        public Task<bool> InsertAsync(SysNotice entity, CancellationToken cancellationToken = default)
        {
            steps.Add("insert");
            LastInserted = entity;
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<bool> UpdateAsync(SysNotice entity, CancellationToken cancellationToken = default)
        {
            steps.Add("update");
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<TDto?> GetAsync<TDto>(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<List<SysNotice>> GetListAsync(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            IDictionary<Expression<Func<SysNotice, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<List<TDto>> GetListAsync<TDto>(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            IDictionary<Expression<Func<SysNotice, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<PagedResponse<SysNotice>> GetPagedAsync(
            PagedRequest request,
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            IDictionary<Expression<Func<SysNotice, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<PagedResponse<TDto>> GetPagedAsync<TDto>(
            PagedRequest request,
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            IDictionary<Expression<Func<SysNotice, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> AnyAsync(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<long> CountAsync(
            IEnumerable<Expression<Func<SysNotice, bool>>>? predicates = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> InsertAsync(IEnumerable<SysNotice> entities, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> HardDeleteAsync(Expression<Func<SysNotice, bool>> predicate, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<SysNotice?> GetDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(Expression<Func<SysNotice, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(Expression<Func<SysNotice, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null)
            => throw new NotSupportedException();
    }

    /// <summary>领域事件发布桩：仅记录发布次序。</summary>
    private sealed class RecordingPublisher(List<string> steps) : IDomainEventPublisher
    {
        /// <inheritdoc />
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : DomainEvent
        {
            steps.Add("publish");
            return Task.CompletedTask;
        }
    }
}
