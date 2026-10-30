namespace Domain.Entities;

using SqlSugar;

/// <summary>
/// 聚合根基类：在 <see cref="DomainBase"/> 之上统一承载单主键、乐观并发版本、软删除与全量审计字段（创建审计与租户归属继承自 DomainBase）。
/// <para>实体本身不保留验证逻辑，请求侧字段约束统一由 Application 层 FluentValidation Validators 承接；
/// setter 仅做纯数据归一化（可空字符串 trim）。审计字段（创建/更新人与时间、软删除标记）由数据访问层自动填充，
/// 业务代码无需显式赋值；<see cref="Version"/> 由聚合仓储在更新时显式做乐观并发校验
/// （UPDATE ... WHERE Version = 期望值，受影响 0 行即并发冲突），冲突时抛出业务异常。</para>
/// </summary>
public abstract class AggregateBase : DomainBase, IEquatable<AggregateBase>
{
    /// <summary>主键 Id（全局统一 36 位 GUID，由数据访问层在插入时自动生成）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public virtual Guid Id { get; set; }

    /// <summary>乐观并发版本号：插入时为 0，每次更新由仓储显式 +1 并以旧值作更新条件；
    /// 仓库内不做框架级版本校验（IsEnableUpdateVersionValidation 与 ExecuteCommandWithOptLock 组合不可靠）。</summary>
    [SugarColumn(IsNullable = false)]
    public virtual long Version { get; set; }

    /// <summary>最后更新时间，从未更新过为 null，由数据访问层自动填充。</summary>
    public virtual DateTime? UpdatedAt { get; set; }

    /// <summary>最后更新人（用户名），由数据访问层自动填充。</summary>
    public virtual string? UpdatedBy { get; set; }

    /// <summary>描述 / 备注。</summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public virtual string? Description { get; set; }

    /// <summary>软删除标记：true 表示已删除，默认查询自动过滤。</summary>
    public virtual bool IsDeleted { get; set; }

    /// <summary>按「类型 + 主键」判定相等；主键为空（未持久化）的瞬态聚合互不相等。</summary>
    public virtual bool Equals(AggregateBase? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other.GetType() == GetType() && Id != Guid.Empty && Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as AggregateBase);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>相等运算符。</summary>
    public static bool operator ==(AggregateBase? left, AggregateBase? right) => left?.Equals(other: right) ?? right is null;

    /// <summary>不等运算符。</summary>
    public static bool operator !=(AggregateBase? left, AggregateBase? right) => !(left == right);
}
