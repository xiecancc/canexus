
using Domain.Entities;
using SqlSugar;

namespace Infrastructure.Tests;
/// <summary>集成测试实体：聚合根（带审计、乐观锁与软删除）。</summary>
[SugarTable("it_soft_delete_entity")]
public class SoftDeleteTestEntity : AggregateBase
{
    /// <summary>业务名称。</summary>
    [SugarColumn(IsNullable = true)]
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
