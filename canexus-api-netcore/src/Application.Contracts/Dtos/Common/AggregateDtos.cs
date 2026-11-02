using Domain.Shared.Paging;

namespace Application.Contracts.Dtos.Common;

#region 新增/更新请求

/// <summary>聚合请求载荷基类（标记）：新增 / 修改载荷的类型约束挂载点，仅承载「聚合写载荷」的语义标记。
/// <para>不含任何公共字段——聚合 id 与乐观锁版本号由命令参数独立承载（与删除命令形态对称，传输层走路由参数），
/// 审计字段由数据访问层生成，均不随载荷提交。</para></summary>
public abstract class AggregateInputRequest
{
}

#endregion

#region 详情响应

/// <summary>
/// 聚合详情响应基类：在 <see cref="AggregatePagedResponse"/> 最小根（Id / Version / CreatedAt / Description）之上，
/// 补充镜像 <c>AggregateBase</c> 的租户归属（TenantId）、创建人（CreatedBy）与更新审计（UpdatedAt / UpdatedBy）。
/// <para>详情 / 树节点等全量读模型以本类为根；前端将 <see cref="AggregatePagedResponse.Version"/> 原样回传即可完成修改 / 删除的乐观锁校验闭环。
/// 软删除标记 IsDeleted 不对外暴露：查询语义已默认过滤已删数据，恢复流程经恢复命令（AggregateRestoreCommand）承载。</para>
/// </summary>
public abstract class AggregateDetailResponse : AggregatePagedResponse
{
    /// <summary>所属租户 Id；null 表示平台级数据（镜像 AggregateBase.TenantId）。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>创建人（用户 Id）（镜像 AggregateBase.CreatedBy）。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>最后更新时间，从未更新过为 null（镜像 AggregateBase.UpdatedAt）。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>最后更新人（用户 Id），从未更新过为 null（镜像 AggregateBase.UpdatedBy）。</summary>
    public Guid? UpdatedBy { get; set; }
}

#endregion

#region 分页请求与响应

/// <summary>
/// 聚合分页查询请求基类：聚合模块分页查询的公共根，继承领域分页契约 <see cref="PagedRequest"/>
/// （页索引 / 页大小的越界收敛规则由其承载）。
/// </summary>
public abstract class AggregatePagedRequest : PagedRequest
{
}

/// <summary>
/// 聚合分页响应基类：聚合读模型最小根，承载前端编辑 / 删除闭环与列表展示所需的公共字段——
/// 聚合 Id（Id）、乐观锁版本号（Version，修改 / 删除请求须原样回传）、创建时间（CreatedAt，列表排序展示）
/// 与备注（Description，全模块共用的业务字段，镜像 AggregateBase.Description）。
/// </summary>
public abstract class AggregatePagedResponse
{
    /// <summary>聚合 Id（镜像 AggregateBase.Id）。</summary>
    public Guid Id { get; set; }

    /// <summary>乐观锁版本号，修改 / 删除请求须原样回传（镜像 AggregateBase.Version）。</summary>
    public Guid Version { get; set; }

    /// <summary>创建时间（镜像 AggregateBase.CreatedAt）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>描述 / 备注（镜像 AggregateBase.Description）。</summary>
    public string? Description { get; set; }
}

#endregion

#region 列表请求与响应

/// <summary>聚合列表查询请求基类：非分页全量列表查询（树形 / 下拉等）的公共根（预留为后续筛选条件的挂载点）。</summary>
public abstract class AggregateListRequest
{
}

/// <summary>聚合列表响应基类：聚合模块轻量读模型（下拉简项等）的公共根，不承载实体标识与审计。</summary>
public abstract class AggregateItemResponse
{
}

#endregion

#region 下拉框响应

public abstract class AggregateDropdownRequest
{
}


/// <summary>聚合下拉响应基类：下拉简项读模型根，仅承载选中值（聚合 Id）与模块自定义展示文本。</summary>
public abstract class AggregateDropdownResponse
{
    /// <summary>聚合 Id（下拉选中值）。</summary>
    public Guid Id { get; set; }
}

#endregion
