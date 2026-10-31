
using Application.Contracts.Common;
using Domain.Shared.Enums;

namespace Application.Contracts.Depts;
/// <summary>部门 VO 公共基类：对齐 SysDept 聚合的业务字段。</summary>
public abstract class DeptBaseVO
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
public class DeptTreeVO : DeptBaseVO
{
    /// <summary>部门 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>子部门列表。</summary>
    public List<DeptTreeVO> Children { get; set; } = [];
}

/// <summary>部门详情（GET /api/system/dept/{id}）。</summary>
public class DeptDetailVO : DeptBaseVO
{
    /// <summary>部门 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>父部门 Id。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>部门写入请求基类：创建 / 更新共同字段（对齐 SysDept 聚合；树形结构由 ParentId 维护）。</summary>
public abstract class DeptWriteRequest : StatusRequest
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
}

/// <summary>创建部门请求（POST /api/system/dept）。</summary>
public class DeptCreateRequest : DeptWriteRequest
{
    /// <summary>父部门 Id；Guid.Empty 表示根。</summary>
    public Guid ParentId { get; set; }
}

/// <summary>更新部门请求（PUT /api/system/dept/{id}；父级不可选自身或自己的后代）。</summary>
public class DeptUpdateRequest : DeptWriteRequest
{
    /// <summary>父部门 Id。</summary>
    public Guid ParentId { get; set; }
}
