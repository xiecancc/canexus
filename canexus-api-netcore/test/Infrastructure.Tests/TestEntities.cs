
using Domain.Entities;
using SqlSugar;

namespace Infrastructure.Tests;
/// <summary>集成测试实体：聚合根（带审计、乐观锁与软删除）。
/// <para>Name 声明软删除感知唯一索引（Name + DeletedKey），用于唯一键槽与并发唯一冲突兜底验证。</para></summary>
[SugarIndex("uk_it_soft_delete_name", nameof(Name), OrderByType.Asc, nameof(DeletedKey), OrderByType.Asc, true)]
[SugarTable("it_soft_delete_entity")]
public class SoftDeleteTestEntity : AggregateBase
{
    /// <summary>业务名称（唯一键，与 DeletedKey 组成软删除感知唯一索引；显式长度避免默认 255 在 utf8mb4 下超出 MyISAM 键长上限）。</summary>
    [SugarColumn(IsNullable = true, Length = 50)]
    public string? Name { get; set; }
}

/// <summary>集成测试实体：多租户聚合根（TenantId 由基类提供）。</summary>
[SugarTable("it_multi_tenant_entity")]
public class MultiTenantTestEntity : AggregateBase
{
    /// <summary>业务名称。</summary>
    [SugarColumn(IsNullable = true)]
    public string? Name { get; set; }
}

/// <summary>集成测试实体：与软删除实体结构一致，用于物理删除路径验证。</summary>
[SugarTable("it_plain_entity")]
public class PlainTestEntity : AggregateBase
{
    /// <summary>业务名称。</summary>
    [SugarColumn(IsNullable = true)]
    public string? Name { get; set; }
}
