using Domain.Shared.Paging;

namespace Application.Contracts.Dtos.Common;
/// <summary>请求载荷根基类：新增 / 修改公共属性的挂载点（实体 Id 与审计字段由数据访问层生成，不随载荷提交）。</summary>
public abstract class DomainChangeRequest
{
}

/// <summary>列表查询请求基类：非分页全量列表查询（树形 / 下拉等）的公共根（预留为后续筛选条件的挂载点）。</summary>
public abstract class DomainListRequest
{
}

/// <summary>列表响应基类：下拉简项等不含实体标识与审计的轻量读模型公共根。</summary>
public abstract class DomainListResponse
{
}

/// <summary>
/// 分页查询请求基类：分页 DTO 家族公共根，继承领域分页契约 <see cref="PagedRequest"/>
/// （页索引 / 页大小的越界收敛规则由其承载）。
/// </summary>
public abstract class DomainPagedRequest : PagedRequest
{
}

/// <summary>
/// 领域实体响应基类：镜像 <c>DomainBase</c> 的共有字段——租户归属与创建审计。
/// </summary>
public abstract class DomainPagedResponse
{
    /// <summary>所属租户 Id；null 表示平台级数据（镜像 DomainBase.TenantId）。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>创建时间（镜像 DomainBase.CreatedAt）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>创建人（用户 Id）（镜像 DomainBase.CreatedBy）。</summary>
    public Guid CreatedBy { get; set; }
}
