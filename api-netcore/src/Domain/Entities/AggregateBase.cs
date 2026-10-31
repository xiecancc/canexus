
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 聚合根基类：在 <see cref="DomainBase"/> 之上统一承载单主键、乐观并发版本、软删除与全量审计字段（创建审计与租户归属继承自 DomainBase）。
/// <para>实体本身不保留验证逻辑，请求侧字段约束统一由 Application 层 FluentValidation Validators 承接；
/// setter 仅做纯数据归一化（可空字符串 trim）。审计字段（创建/更新人与时间、软删除标记）由数据访问层自动填充，
/// 业务代码无需显式赋值；<see cref="Version"/> 为乐观并发版本令牌，聚合仓储更新时经
/// SqlSugar ExecuteCommandWithOptLock 自动校验（UPDATE ... WHERE Version = 传入值，受影响 0 行即并发冲突），
/// 冲突时抛出业务异常。</para>
/// </summary>
public abstract class AggregateBase : DomainBase, IEquatable<AggregateBase>
{
    /// <summary>主键 Id（全局统一 36 位 GUID，由数据访问层在插入时自动生成）。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public virtual Guid Id { get; set; }

    /// <summary>乐观并发版本令牌：插入时由默认值生成新 Guid，更新时由框架自动以旧值作更新条件并轮换为新 Guid
    /// （SqlSugar 官方推荐 Guid 类型，避免 long 前端序列化精度丢失），
    /// 经 <c>AggregateRepository.UpdateAsync</c> 走 ExecuteCommandWithOptLock 框架级乐观锁。</summary>
    [SugarColumn(ColumnDescription = "版本号", IsNullable = false, IsEnableUpdateVersionValidation = true)]
    public virtual Guid Version { get; set; } = Guid.NewGuid();

    /// <summary>最后更新时间，从未更新过为 null，由数据访问层自动填充。</summary>
    public virtual DateTime? UpdatedAt { get; set; }

    /// <summary>最后更新人（用户 Id），从未更新过为 null，由数据访问层自动填充。</summary>
    public virtual Guid? UpdatedBy { get; set; }

    /// <summary>描述 / 备注。</summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public virtual string? Description { get; set; }

    /// <summary>软删除标记：true 表示已删除，默认查询自动过滤。</summary>
    public virtual bool IsDeleted { get; set; }

    /// <summary>相等运算符。</summary>
    public static bool operator ==(AggregateBase? left, AggregateBase? right) => left?.Equals(other: right) ?? right is null;

    /// <summary>不等运算符。</summary>
    public static bool operator !=(AggregateBase? left, AggregateBase? right) => !(left == right);

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
}
