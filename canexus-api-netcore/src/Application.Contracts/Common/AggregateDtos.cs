namespace Application.Contracts.Common;
/// <summary>聚合请求载荷基类：新增 / 修改公共属性的挂载点（聚合 Id 与审计字段由数据访问层生成，不随载荷提交）。</summary>
public abstract class AggregateChangeRequest : DomainChangeRequest
{
    /// <summary>乐观锁版本号：修改类请求须原样回传列表 / 详情响应携带的版本（须与服务端聚合当前版本一致才允许修改）；
    /// 创建请求无需提交，实体 Version 由数据访问层生成并轮换。</summary>
    public Guid Version { get; set; }
}

public abstract class AggregateDetailResponse : DomainDetailResponse
{

}

/// <summary>聚合列表查询请求基类：非分页全量列表查询（树形 / 下拉等）的公共根（预留为后续筛选条件的挂载点）。</summary>
public abstract class AggregateListRequest : DomainListRequest
{
}

/// <summary>聚合分页查询请求基类：聚合模块分页查询的公共根。</summary>
public abstract class AggregatePagedRequest : DomainPagedRequest
{
}

/// <summary>聚合列表响应基类：聚合模块轻量读模型的公共根（租户归属与创建审计由 <see cref="DomainPagedResponse"/> 承载）。</summary>
public abstract class AggregateListResponse : DomainListResponse
{
}

/// <summary>
/// 聚合实体响应基类：镜像 <c>AggregateBase</c> 的标识（Id）、乐观锁版本（Version）、更新审计（UpdatedAt / UpdatedBy）
/// 与备注（Description）；租户归属与创建审计继承自 <see cref="DomainPagedResponse"/>（镜像 DomainBase）。
/// <para>分页查询将其派生类作为 <see cref="Domain.Shared.Paging.PagedResponse{T}"/> 的泛型参数返回；
/// 前端将 <see cref="Version"/> 原样回传即可完成修改 / 删除的乐观锁校验闭环。
/// 软删除标记 IsDeleted 不对外暴露：查询语义已默认过滤已删数据，恢复流程经恢复命令（AggregateRestoreCommand）承载。</para>
/// </summary>
public abstract class AggregatePagedResponse : DomainPagedResponse
{
    /// <summary>聚合 Id（镜像 AggregateBase.Id）。</summary>
    public Guid Id { get; set; }

    /// <summary>乐观锁版本号，修改 / 删除请求须原样回传（镜像 AggregateBase.Version）。</summary>
    public Guid Version { get; set; }

    /// <summary>最后更新时间，从未更新过为 null（镜像 AggregateBase.UpdatedAt）。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>最后更新人（用户 Id），从未更新过为 null（镜像 AggregateBase.UpdatedBy）。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>描述 / 备注（镜像 AggregateBase.Description）。</summary>
    public string? Description { get; set; }
}
