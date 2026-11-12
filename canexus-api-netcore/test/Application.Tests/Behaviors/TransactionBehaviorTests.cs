
using Application.Behaviors;
using Application.Contracts.Abstractions;
using Application.Contracts.Commands;
using Application.Events;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Application.Tests.Behaviors;
/// <summary>用于测试的工作单元假实现：记录事务包裹次数，委托直接执行（不开启真实事务）。</summary>
public sealed class RecordingUnitOfWork : IUnitOfWork
{
    /// <summary>事务包裹调用次数。</summary>
    public int TransactionCount { get; private set; }

    /// <inheritdoc />
    public Task<T> RunInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        TransactionCount++;
        return action();
    }

    /// <inheritdoc />
    public Task RunInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        return RunInTransactionAsync<object?>(
            async () =>
            {
                await action();
                return null;
            },
            cancellationToken);
    }
}

/// <summary>用于测试的写命令（实现 ICommand 标记）。</summary>
public sealed record TestTransactionCommand : Request<bool>, ICommand;

/// <summary>用于测试的非事务写命令（实现 ICommand + INonTransactionalCommand 标记）。</summary>
public sealed record TestNonTransactionalCommand : Request<bool>, ICommand, INonTransactionalCommand;

/// <summary>用于测试的读请求（不实现 ICommand 标记）。</summary>
public sealed record TestTransactionQuery : Request<string>;

/// <summary>用于测试的投递服务假实现：直接完成（本测试仅验证事务包裹语义，投递行为由集成测试覆盖）。</summary>
public sealed class NoOpOutboxDeliveryService : IOutboxDeliveryService
{
    /// <inheritdoc />
    public Task DeliverAsync(IReadOnlyList<OutboxMessage> messages, string? claimOwner, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>事务管道行为测试：写命令自动包裹事务、查询透放、异常原样上抛。</summary>
public class TransactionBehaviorTests
{
    /// <summary>验证写命令（ICommand）经事务包裹执行并返回处理结果。</summary>
    [Test]
    public async Task Handle_Command_WrapsInTransactionAndReturnsResponse()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var behavior = new TransactionBehavior<TestTransactionCommand, bool>(unitOfWork, new PendingOutboxDelivery(), new NoOpOutboxDeliveryService(), NullLogger<TransactionBehavior<TestTransactionCommand, bool>>.Instance);
        var next = new RequestHandlerDelegate<bool>(_ => Task.FromResult(true));

        var response = await behavior.Handle(new TestTransactionCommand(), next, CancellationToken.None);

        Assert.That(response, Is.True);
        Assert.That(unitOfWork.TransactionCount, Is.EqualTo(1), "写命令应恰好包裹一次事务");
    }

    /// <summary>验证读请求直接透放，不开启事务。</summary>
    [Test]
    public async Task Handle_Query_PassesThroughWithoutTransaction()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var behavior = new TransactionBehavior<TestTransactionQuery, string>(unitOfWork, new PendingOutboxDelivery(), new NoOpOutboxDeliveryService(), NullLogger<TransactionBehavior<TestTransactionQuery, string>>.Instance);
        var next = new RequestHandlerDelegate<string>(_ => Task.FromResult("ok"));

        var response = await behavior.Handle(new TestTransactionQuery(), next, CancellationToken.None);

        Assert.That(response, Is.EqualTo("ok"));
        Assert.That(unitOfWork.TransactionCount, Is.EqualTo(0), "读请求不应开启事务");
    }

    /// <summary>验证非事务写命令（INonTransactionalCommand）直接透放，不开启事务（P0-D1）。</summary>
    [Test]
    public async Task Handle_NonTransactionalCommand_PassesThroughWithoutTransaction()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var behavior = new TransactionBehavior<TestNonTransactionalCommand, bool>(unitOfWork, new PendingOutboxDelivery(), new NoOpOutboxDeliveryService(), NullLogger<TransactionBehavior<TestNonTransactionalCommand, bool>>.Instance);
        var next = new RequestHandlerDelegate<bool>(_ => Task.FromResult(true));

        var response = await behavior.Handle(new TestNonTransactionalCommand(), next, CancellationToken.None);

        Assert.That(response, Is.True);
        Assert.That(unitOfWork.TransactionCount, Is.EqualTo(0), "非事务写命令不应开启数据库事务");
    }

    /// <summary>验证写命令处理器抛出异常时原样上抛（回滚语义由真实 IUnitOfWork 保证，此处验证不吞异常）。</summary>
    [Test]
    public void Handle_CommandThrows_PropagatesException()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var behavior = new TransactionBehavior<TestTransactionCommand, bool>(unitOfWork, new PendingOutboxDelivery(), new NoOpOutboxDeliveryService(), NullLogger<TransactionBehavior<TestTransactionCommand, bool>>.Instance);
        var next = new RequestHandlerDelegate<bool>(_ => throw new InvalidOperationException("业务异常"));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await behavior.Handle(new TestTransactionCommand(), next, CancellationToken.None));
        Assert.That(unitOfWork.TransactionCount, Is.EqualTo(1), "异常路径同样应已进入事务包裹");
    }

    /// <summary>P0-D1 架构守卫：登录命令必须保持非事务标记，刷新令牌轮换保持事务语义。</summary>
    [Test]
    public void AuthCommands_TransactionalMarkers_AreAligned()
    {
        Assert.Multiple(() =>
        {
            // 登录主体耗时在事务外（BCrypt / Redis），不得被事务包裹；误删标记会重新放大连接占用
            Assert.That(typeof(AuthLoginCommand).IsAssignableTo(typeof(INonTransactionalCommand)), Is.True,
                "登录命令必须保持 INonTransactionalCommand 标记（P0-D1：BCrypt 不得进入数据库事务）");
            // 刷新令牌轮换「吊销旧令牌 + 签发新令牌」需原子完成，必须保持事务语义
            Assert.That(typeof(AuthRefreshTokenCommand).IsAssignableTo(typeof(INonTransactionalCommand)), Is.False,
                "刷新令牌命令不得标记非事务（轮换需原子吊销旧令牌并签发新令牌）");
        });
    }
}
