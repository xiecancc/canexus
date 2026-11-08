using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Domain.Tests.Paging;

/// <summary>
/// 分页请求副本单测：逐页取数必须复制请求对象而不是改写共享实例，且筛选条件需完整保留。
/// </summary>
[TestFixture]
public class PagedRequestCloneTests
{
    /// <summary>复制后返回同类型实例，页参数按入参设置。</summary>
    [Test]
    public void CloneForPage_ReturnsSameTypeWithRequestedPaging()
    {
        var original = new TestPageRequest { Keyword = "admin", PageIndex = 1, PageSize = 10 };

        var clone = original.CloneForPage<TestPageRequest>(7, 500);

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.TypeOf<TestPageRequest>());
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.PageIndex, Is.EqualTo(7));
            Assert.That(clone.PageSize, Is.EqualTo(500));
        });
    }

    /// <summary>复制不得改动原对象的页参数，避免导出把分页口径回写给调用方。</summary>
    [Test]
    public void CloneForPage_DoesNotMutateOriginal()
    {
        var original = new TestPageRequest { PageIndex = 3, PageSize = 25 };

        _ = original.CloneForPage<TestPageRequest>(2, 500);

        Assert.Multiple(() =>
        {
            Assert.That(original.PageIndex, Is.EqualTo(3));
            Assert.That(original.PageSize, Is.EqualTo(25));
        });
    }

    /// <summary>派生类上的筛选字段（关键字 / 部门 / 状态）随副本完整保留。</summary>
    [Test]
    public void CloneForPage_PreservesDerivedFilters()
    {
        var deptId = Guid.NewGuid();
        var original = new TestPageRequest
        {
            Keyword = "  张三  ",
            DeptId = deptId,
            Status = CommonStatus.Disabled
        };

        var clone = original.CloneForPage<TestPageRequest>(4, 500);

        Assert.Multiple(() =>
        {
            Assert.That(clone.Keyword, Is.EqualTo(original.Keyword));
            Assert.That(clone.DeptId, Is.EqualTo(deptId));
            Assert.That(clone.Status, Is.EqualTo(CommonStatus.Disabled));
        });
    }

    /// <summary>页大小越界仍由 PagedRequest 的既有规则收敛为 500 / 默认值。</summary>
    [Test]
    public void CloneForPage_StillAppliesPagingBounds()
    {
        var original = new TestPageRequest();

        Assert.Multiple(() =>
        {
            Assert.That(original.CloneForPage<TestPageRequest>(0, 5000).PageSize, Is.EqualTo(500));
            Assert.That(original.CloneForPage<TestPageRequest>(2, 0).PageSize, Is.EqualTo(10));
            Assert.That(original.CloneForPage<TestPageRequest>(-1, 20).PageIndex, Is.EqualTo(1));
        });
    }

    /// <summary>泛型实参与运行时类型不匹配时显式失败，避免静默产生错误类型的请求。</summary>
    [Test]
    public void CloneForPage_WrongGenericType_Throws()
    {
        var original = new TestPageRequest();

        Assert.Throws<InvalidCastException>(() =>
        {
            _ = original.CloneForPage<OtherPageRequest>(1, 500);
        });
    }

    /// <summary>携带派生筛选字段的分页请求替身。</summary>
    private sealed class TestPageRequest : PagedRequest
    {
        /// <summary>关键字筛选。</summary>
        public string? Keyword { get; set; }

        /// <summary>部门筛选。</summary>
        public Guid? DeptId { get; set; }

        /// <summary>状态筛选。</summary>
        public CommonStatus? Status { get; set; }
    }

    /// <summary>用于验证类型不匹配场景的替身类型。</summary>
    private sealed class OtherPageRequest : PagedRequest
    {
    }
}
