
using Application.Contracts.Common;
using Domain.Shared.Enums;

namespace Application.Contracts.Depts;
/// <summary>部门响应公共基类：对齐 SysDept 聚合的业务字段（Id / Version / CreatedAt 由 <see cref="AggregatePagedResponse"/> 承载）。</summary>
public abstract class DeptBaseResponse : AggregatePagedResponse
{
    /// <summary>部门名称。</summary>
    public string DeptName { get; set; } = string.Empty;

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>负责人。</summary>
    public string? Leader { get; set; }

    /// <summary>联系电话。</summary>
    public string? Phone { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}

/// <summary>部门树节点（GET /api/system/dept/tree；Children 由树构建器组装）。</summary>
public class DeptTreeResponse : DeptBaseResponse
{
    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }

    /// <summary>子部门列表。</summary>
    public List<DeptTreeResponse> Children { get; set; } = [];
}

/// <summary>部门详情（GET /api/system/dept/{id}）。</summary>
public class DeptDetailResponse : DeptBaseResponse
{
    /// <summary>父部门 Id。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>部门写入请求基类：创建 / 更新共同字段（镜像 SysDept 聚合的启停 / 备注与业务字段；树形结构由 ParentId 维护）。</summary>
public abstract class DeptRequest : AggregateChangeRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>部门名称。</summary>
    public string DeptName { get; set; } = string.Empty;

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>负责人。</summary>
    public string? Leader { get; set; }

    /// <summary>联系电话。</summary>
    public string? Phone { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }
}

/// <summary>创建部门请求（POST /api/system/dept）。</summary>
public class DeptCreateRequest : DeptRequest
{
    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>更新部门请求（PUT /api/system/dept/{id}；父级不可选自身或自己的后代；Version 须回传乐观锁版本号）。</summary>
public class DeptUpdateRequest : DeptRequest
{
    /// <summary>乐观锁版本号（须与服务端聚合当前版本一致才允许修改）。</summary>
    public Guid Version { get; set; }

    /// <summary>父部门 Id。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>删除部门请求（DELETE /api/system/dept/{id}；Version 须回传乐观锁版本号）。</summary>
public class DeptDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
}
