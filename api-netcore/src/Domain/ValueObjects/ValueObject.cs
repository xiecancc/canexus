namespace Domain.ValueObjects;

/// <summary>
/// 值对象基类：无唯一标识，按属性分量值判定相等性。
/// <para>子类通过重写 <see cref="GetEqualityComponents"/> 声明参与相等性比较的属性（按声明顺序）。</para>
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    private const int HashCodeSeed = 0x61E04917;

    /// <summary>返回参与相等性比较的属性分量（按声明顺序枚举）。</summary>
    /// <returns>属性分量序列。</returns>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public virtual bool Equals(ValueObject? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // 值对象相等性要求同类型且各分量依次相等
        return other.GetType() == GetType() && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    /// <inheritdoc />
    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(HashCodeSeed, (current, component) => HashCode.Combine(current, component));

    /// <summary>相等运算符。</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) => left?.Equals(right) ?? right is null;

    /// <summary>不等运算符。</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
