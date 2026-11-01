namespace Application.Contracts.Auth;

/// <summary>路由元信息（前端渲染菜单 / 页面缓存使用）。</summary>
public class RouteMeta
{
    /// <summary>菜单标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>菜单图标。</summary>
    public string? Icon { get; set; }

    /// <summary>页面是否缓存（KeepAlive）。</summary>
    public bool KeepAlive { get; set; }

    /// <summary>是否隐藏。</summary>
    public bool Hidden { get; set; }

    /// <summary>显示顺序。</summary>
    public int Order { get; set; }
}

/// <summary>前端路由视图对象（/api/auth/routes 响应数据，完整菜单树）。</summary>
public class RouteResponse
{
    /// <summary>路由名（驼峰，全局唯一）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>路由地址（顶层带 / 前缀，子级相对）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>组件路径（相对 views 目录，如 system/user/index）；目录为 null。</summary>
    public string? Component { get; set; }

    /// <summary>重定向地址（目录且有子级时为第一个子级完整路径）。</summary>
    public string? Redirect { get; set; }

    /// <summary>路由元信息。</summary>
    public RouteMeta Meta { get; set; } = new();

    /// <summary>子路由。</summary>
    public List<RouteResponse> Children { get; set; } = [];
}
