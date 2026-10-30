namespace Domain.Tests.Entities;

using Domain.Entities;
using Domain.ValueObjects;

/// <summary>用于测试的聚合根实体。</summary>
public class TestAggregate : AggregateBase
{
}

/// <summary>用于测试的派生聚合（验证类型参与相等性判定）。</summary>
public class DerivedTestAggregate : TestAggregate
{
}

/// <summary>聚合根基类相等性单元测试。</summary>
[TestFixture]
public class AggregateBaseTests
{
    private static readonly Guid SameId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void Equals_SameTypeAndSameId_ReturnsTrue()
    {
        // 同类型同主键的两个实例视为相等（值语义）
        var left = new TestAggregate { Id = SameId };
        var right = new TestAggregate { Id = SameId };

        Assert.Multiple(() =>
        {
            Assert.That(left.Equals(right), Is.True);
            Assert.That(left == right, Is.True);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        });
    }

    [Test]
    public void Equals_DifferentId_ReturnsFalse()
    {
        // 主键不同则不相等
        var left = new TestAggregate { Id = SameId };
        var right = new TestAggregate { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };

        Assert.Multiple(() =>
        {
            Assert.That(left.Equals(right), Is.False);
            Assert.That(left != right, Is.True);
        });
    }

    [Test]
    public void Equals_DifferentType_ReturnsFalse()
    {
        // 类型不同（含派生关系）不视为相等
        var left = new TestAggregate { Id = SameId };
        var right = new DerivedTestAggregate { Id = SameId };

        Assert.That(left.Equals(right), Is.False);
    }

    [Test]
    public void Equals_TransientEntities_AreNotEqual()
    {
        // 未持久化（主键为空 Guid）的瞬态聚合彼此不相等
        var left = new TestAggregate();
        var right = new TestAggregate();

        Assert.Multiple(() =>
        {
            Assert.That(left.Id, Is.EqualTo(Guid.Empty));
            Assert.That(left.Equals(right), Is.False);
            Assert.That(left != right, Is.True);
        });
    }

    [Test]
    public void Equals_NullOrOtherObject_ReturnsFalse()
    {
        // 与 null 或无关对象比较返回 false
        TestAggregate entity = new() { Id = SameId };

        Assert.Multiple(() =>
        {
            Assert.That(entity.Equals(null), Is.False);
            Assert.That(entity == null, Is.False);
        });
    }

    [Test]
    public void Equals_SameReference_ReturnsTrue()
    {
        // 引用相等直接返回 true（即使主键为空）
        var entity = new TestAggregate();

        Assert.Multiple(() =>
        {
            Assert.That(entity.Equals(entity), Is.True);
            Assert.That(entity.Equals((object)entity), Is.True);
        });
    }

    [Test]
    public void TransientEntity_NullComparison_WorksViaOperators()
    {
        // 空主键瞬态聚合与 null 比较：== 为 false，!= 为 true
        var entity = new TestAggregate();

        Assert.Multiple(() =>
        {
            Assert.That(entity == null, Is.False);
            Assert.That(entity != null, Is.True);
            Assert.That((TestAggregate?)null == null, Is.True, "双 null 相等");
        });
    }

    [Test]
    public void AuditFields_DefaultValues_AreSensible()
    {
        // 审计字段默认值：乐观锁版本 0、未删除、审计人/时间为空
        var entity = new TestAggregate();

        Assert.Multiple(() =>
        {
            Assert.That(entity.Version, Is.EqualTo(0L));
            Assert.That(entity.IsDeleted, Is.False);
            Assert.That(entity.TenantId, Is.Null);
            Assert.That(entity.CreatedBy, Is.Null);
            Assert.That(entity.UpdatedAt, Is.Null);
            Assert.That(entity.UpdatedBy, Is.Null);
            Assert.That(entity.Description, Is.Null);
        });
    }
}

/// <summary>用于测试的地址值对象。</summary>
public class TestAddress : ValueObject
{
    /// <summary>省份。</summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>城市。</summary>
    public string City { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Province;
        yield return City;
    }
}

/// <summary>用于测试的单分量值对象（覆盖 HashCode 累加路径）。</summary>
public class TestSingleValue : ValueObject
{
    /// <summary>值。</summary>
    public int Value { get; set; }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}

/// <summary>值对象相等性单元测试。</summary>
[TestFixture]
public class ValueObjectTests
{
    [Test]
    public void Equals_SameComponentValues_ReturnsTrue()
    {
        // 分量值相同即相等（与实例无关）
        var left = new TestAddress { Province = "广东", City = "深圳" };
        var right = new TestAddress { Province = "广东", City = "深圳" };

        Assert.Multiple(() =>
        {
            Assert.That(left.Equals(right), Is.True);
            Assert.That(left == right, Is.True);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        });
    }

    [Test]
    public void Equals_AnyComponentDiffers_ReturnsFalse()
    {
        // 任一分量不同即不相等
        var left = new TestAddress { Province = "广东", City = "深圳" };
        var right = new TestAddress { Province = "广东", City = "广州" };

        Assert.Multiple(() =>
        {
            Assert.That(left.Equals(right), Is.False);
            Assert.That(left != right, Is.True);
        });
    }

    [Test]
    public void Equals_DifferentType_ReturnsFalse()
    {
        // 类型不同不视为相等
        var left = new TestAddress { Province = "广东", City = "深圳" };
        var right = new TestSingleValue { Value = 1 };

        Assert.That(left.Equals(right), Is.False);
    }

    [Test]
    public void Equals_Null_ReturnsFalse()
    {
        // 与 null 比较返回 false
        var left = new TestAddress { Province = "广东", City = "深圳" };

        Assert.Multiple(() =>
        {
            Assert.That(left.Equals(null), Is.False);
            Assert.That(left == null, Is.False);
        });
    }

    [Test]
    public void Equals_NullComponent_MatchesNullValue()
    {
        // 分量为 null 时按 null 值比较
        var left = new TestAddress { Province = "广东", City = null! };
        var right = new TestAddress { Province = "广东", City = null! };

        Assert.That(left.Equals(right), Is.True);
    }
}
