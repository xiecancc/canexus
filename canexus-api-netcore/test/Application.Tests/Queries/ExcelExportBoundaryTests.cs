using Application.Contracts.Queries;
using Application.Contracts.Users;
using Application.Queries;
using Domain.Shared.Paging;
using MediatR;

namespace Application.Tests.Queries;

/// <summary>
/// 导出边界单测：验证 10000 行硬裁剪、截断文件名标记、逐页请求对象隔离、取消令牌透传与首页前取消检查。
/// 经公开的 UserExportQueryHandler.Handle 走完整导出链路，无需访问内部支撑类。
/// </summary>
[TestFixture]
public class ExcelExportBoundaryTests
{
    private const int PageRows = 500;
    private const int MaxRows = 10000;

    /// <summary>总数远超上限时只拉 20 页、导出 10000 行，文件名如实标注截断行数。</summary>
    [Test]
    public async Task Export_TotalBeyondLimit_CapsRowsAndMarksFileName()
    {
        var sender = new PagedUserSender(25000, PageRows);
        var original = new UserPageRequest { Keyword = "keep", PageIndex = 9, PageSize = 25 };
        var handler = new UserExportQueryHandler(sender);

        var file = await handler.Handle(new UserExportQuery(original), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(file.FileName, Does.Contain($"_已截断{MaxRows}行"));
            Assert.That(sender.Requests.Count, Is.EqualTo(MaxRows / PageRows));
            Assert.That(sender.Requests.All(item => item.PageSize == PageRows), Is.True);
            Assert.That(sender.Requests.All(item => item.Keyword == "keep"), Is.True);
            Assert.That(sender.Requests.Distinct().Count(), Is.EqualTo(sender.Requests.Count));
            Assert.That(original.PageIndex, Is.EqualTo(9));
            Assert.That(original.PageSize, Is.EqualTo(25));
            Assert.That(file.Content.Length, Is.GreaterThan(0));
        });
    }

    /// <summary>单页返回超过 500 行时仍不越过总行数上限（累计追加按剩余额度裁剪）。</summary>
    [Test]
    public async Task Export_SinglePageOverPageRows_StillCappedAtMaxRows()
    {
        var sender = new PagedUserSender(12000, 600);
        var handler = new UserExportQueryHandler(sender);

        var file = await handler.Handle(new UserExportQuery(new UserPageRequest()), CancellationToken.None);

        Assert.That(file.FileName, Does.Contain($"_已截断{MaxRows}行"));
    }

    /// <summary>未截断时文件名保持历史格式，不附加标记。</summary>
    [Test]
    public async Task Export_WithinLimit_FileNameHasNoTruncationMarker()
    {
        var sender = new PagedUserSender(700, PageRows);
        var handler = new UserExportQueryHandler(sender);

        var file = await handler.Handle(new UserExportQuery(new UserPageRequest()), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(file.FileName, Does.Not.Contain("_已截断"));
            Assert.That(sender.Requests.Count, Is.EqualTo(2));
        });
    }

    /// <summary>取消令牌逐页透传至 ISender，客户端断开后可中止后续分页。</summary>
    [Test]
    public async Task Export_PassesCancellationTokenToEveryPage()
    {
        using var cts = new CancellationTokenSource();
        var sender = new PagedUserSender(1200, PageRows);
        var handler = new UserExportQueryHandler(sender);

        await handler.Handle(new UserExportQuery(new UserPageRequest()), cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Tokens.Count, Is.EqualTo(3));
            Assert.That(sender.Tokens.All(token => token == cts.Token), Is.True);
        });
    }

    /// <summary>首页前已取消时不再发出任何分页请求，避免已断开的请求继续打满数据库。</summary>
    [Test]
    public void Export_AlreadyCancelled_DoesNotFetchFirstPage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sender = new PagedUserSender(1000, PageRows);
        var handler = new UserExportQueryHandler(sender);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await handler.Handle(new UserExportQuery(new UserPageRequest()), cts.Token));
        Assert.That(sender.Requests, Is.Empty);
    }

    /// <summary>按页返回可控行数的分页 ISender 桩，记录每页请求对象与令牌。</summary>
    private sealed class PagedUserSender(long total, int pageSize) : ISender
    {
        /// <summary>逐页收到的请求对象（用于断言隔离与筛选条件保留）。</summary>
        public List<UserPageRequest> Requests { get; } = [];

        /// <summary>逐页收到的取消令牌。</summary>
        public List<CancellationToken> Tokens { get; } = [];

        /// <inheritdoc />
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not UserQueryPaged query || typeof(TResponse) != typeof(PagedResponse<UserPagedResponse>))
            {
                throw new NotSupportedException($"未预期的请求类型：{request?.GetType().Name}");
            }

            Requests.Add(query.Request);
            Tokens.Add(cancellationToken);
            var count = (int)Math.Min(pageSize, Math.Max(0L, total - (long)(query.Request.PageIndex - 1) * pageSize));
            var items = Enumerable.Range(0, count)
                .Select(index => new UserPagedResponse { UserName = $"u{query.Request.PageIndex}-{index}" })
                .ToList();
            var result = PagedResponse<UserPagedResponse>.Create(items, total, query.Request);
            return Task.FromResult((TResponse)(object)result);
        }

        /// <inheritdoc />
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
