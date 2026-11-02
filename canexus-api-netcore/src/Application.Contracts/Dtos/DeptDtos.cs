using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;

namespace Application.Contracts.Depts;
/// <summary>部门树节点（GET /api/system/dept/tree）：直接继承聚合详情根，业务字段平铺；Children 由树构建器组装。</summary>
public class DeptTreeResponse : AggregateDetailResponse
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

    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }

    /// <summary>子部门列表。</summary>
    public List<DeptTreeResponse> Children { get; set; } = [];
}

/// <summary>部门详情（GET /api/system/dept/{id}）：直接继承聚合详情根，业务字段平铺。</summary>
public class DeptDetailResponse : AggregateDetailResponse
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

    /// <summary>父部门 Id。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>部门写入请求（POST / PUT 共用载荷；镜像 SysDept 聚合的启停 / 备注与业务字段；树形结构由 ParentId 维护）。</summary>
public class DeptInputRequest : AggregateInputRequest
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

    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }
}
