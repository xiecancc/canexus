namespace Application.Contracts.Menus;

using Application.Contracts.Common;
using Domain.Shared.Enums;

/// <summary>菜单 VO 公共基类：对齐 SysMenu 聚合的业务字段（树形结构，无备注字段）。</summary>
public abstract class MenuBaseVO
{
    /// <summary>父菜单 Id；Guid.Empty 表示根节点。</summary>
    public Guid ParentId { get; set; }

    /// <summary>菜单类型（见 <see cref="Domain.Shared.Enums.MenuType"/>）。</summary>
    public MenuType MenuType { get; set; }

    /// <summary>菜单名称。</summary>
    public string MenuName { get; set; } = string.Empty;

    /// <summary>路由地址。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>组件路径（仅菜单类型有值）。</summary>
    public string? Component { get; set; }

    /// <summary>权限标识（如 system:user:list）。</summary>
    public string? Perms { get; set; }

    /// <summary>菜单图标。</summary>
    public string? Icon { get; set; }

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>是否显示（见 <see cref="Domain.Shared.Enums.MenuVisible"/>）。</summary>
    public MenuVisible Visible { get; set; }

    /// <summary>路由页面是否缓存（KeepAlive）。</summary>
    public bool KeepAlive { get; set; }

    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }
}

/// <summary>菜单树节点（GET /api/system/menu/tree，含目录 / 菜单 / 按钮全部类型）。</summary>
public class MenuTreeVO : MenuBaseVO
{
    /// <summary>菜单 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>子菜单列表（按显示顺序升序）。</summary>
    public List<MenuTreeVO> Children { get; set; } = [];
}

/// <summary>菜单详情（GET /api/system/menu/{id}）。</summary>
public class MenuDetailVO : MenuBaseVO
{
    /// <summary>菜单 Id。</summary>
    public Guid Id { get; set; }
}

/// <summary>菜单写入请求基类：创建 / 更新共同字段（对齐 SysMenu 聚合；无备注字段）。</summary>
public abstract class MenuWriteRequest : StatusRequest
{
    /// <summary>父菜单 Id；Guid.Empty 表示顶级。</summary>
    public Guid ParentId { get; set; }

    /// <summary>菜单类型（见 <see cref="Domain.Shared.Enums.MenuType"/>）。</summary>
    public MenuType MenuType { get; set; }

    /// <summary>菜单名称。</summary>
    public string MenuName { get; set; } = string.Empty;

    /// <summary>路由地址（目录与菜单必填）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>组件路径（菜单类型必填，目录与按钮为空）。</summary>
    public string? Component { get; set; }

    /// <summary>权限标识（按钮必填，其他类型可为空）。</summary>
    public string? Perms { get; set; }

    /// <summary>菜单图标。</summary>
    public string? Icon { get; set; }

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }

    /// <summary>是否显示（见 <see cref="Domain.Shared.Enums.MenuVisible"/>）。</summary>
    public MenuVisible Visible { get; set; } = MenuVisible.Show;

    /// <summary>路由页面是否缓存（KeepAlive）。</summary>
    public bool KeepAlive { get; set; }
}

/// <summary>创建菜单请求（POST /api/system/menu）。</summary>
public class MenuCreateRequest : MenuWriteRequest
{
}

/// <summary>更新菜单请求（PUT /api/system/menu/{id}；父级不可选自身或自己的后代）。</summary>
public class MenuUpdateRequest : MenuWriteRequest
{
}
