using System.Reflection;

namespace Domain.Shared.Permissions;

/// <summary>
/// 权限标识集中注册表（P2-B4）：全部权限点以常量集中声明，替代散落在 [RequirePermission] 特性上的字面字符串。
/// <para>新增权限必须在此声明并在控制器上引用常量：控制器引用常量 → 编译期检查；
/// 启动期由 <see cref="PermissionRegistryValidator"/> 扫描控制器声明与注册表比对 → 启动期检查。</para>
/// <para>命名约定：模块:动作:资源（如 system:user:list）；All 由反射自常量生成，永不失配。</para>
/// </summary>
public static class PermissionDefinitions
{
    // —— monitor：操作日志 / 登录日志 / 在线会话 / 任务 / 服务器 / 变更日志 ——

    /// <summary>监控-操作日志-列表。</summary>
    public const string MonitorOperlogList = "monitor:operlog:list";

    /// <summary>监控-操作日志-删除。</summary>
    public const string MonitorOperlogRemove = "monitor:operlog:remove";

    /// <summary>监控-操作日志-清空。</summary>
    public const string MonitorOperlogClean = "monitor:operlog:clean";

    /// <summary>监控-登录日志-列表。</summary>
    public const string MonitorLogininforList = "monitor:logininfor:list";

    /// <summary>监控-登录日志-删除。</summary>
    public const string MonitorLogininforRemove = "monitor:logininfor:remove";

    /// <summary>监控-登录日志-清空。</summary>
    public const string MonitorLogininforClean = "monitor:logininfor:clean";

    /// <summary>监控-在线会话-列表。</summary>
    public const string MonitorOnlineList = "monitor:online:list";

    /// <summary>监控-在线会话-强制下线。</summary>
    public const string MonitorOnlineForceLogout = "monitor:online:forceLogout";

    /// <summary>监控-定时任务-列表。</summary>
    public const string MonitorJobList = "monitor:job:list";

    /// <summary>监控-定时任务-查询。</summary>
    public const string MonitorJobQuery = "monitor:job:query";

    /// <summary>监控-定时任务-新增。</summary>
    public const string MonitorJobAdd = "monitor:job:add";

    /// <summary>监控-定时任务-编辑。</summary>
    public const string MonitorJobEdit = "monitor:job:edit";

    /// <summary>监控-定时任务-删除。</summary>
    public const string MonitorJobRemove = "monitor:job:remove";

    /// <summary>监控-定时任务-恢复。</summary>
    public const string MonitorJobRestore = "monitor:job:restore";

    /// <summary>监控-定时任务-执行一次。</summary>
    public const string MonitorJobRun = "monitor:job:run";

    /// <summary>监控-定时任务-启停状态切换。</summary>
    public const string MonitorJobChangeStatus = "monitor:job:changeStatus";

    /// <summary>监控-服务器监控-列表。</summary>
    public const string MonitorServerList = "monitor:server:list";

    /// <summary>监控-缓存监控-列表。</summary>
    public const string MonitorCacheList = "monitor:cache:list";

    /// <summary>监控-操作日志-导出。</summary>
    public const string MonitorOperlogExport = "monitor:operlog:export";

    /// <summary>监控-登录日志-导出。</summary>
    public const string MonitorLogininforExport = "monitor:logininfor:export";

    /// <summary>监控-实体变更日志-列表。</summary>
    public const string MonitorChangelogList = "monitor:changelog:list";

    // —— system：用户 / 角色 / 菜单 / 部门 / 字典 / 配置 / 租户 / 公告 / 文件 ——

    /// <summary>系统-用户-列表。</summary>
    public const string SystemUserList = "system:user:list";

    /// <summary>系统-用户-查询。</summary>
    public const string SystemUserQuery = "system:user:query";

    /// <summary>系统-用户-新增。</summary>
    public const string SystemUserAdd = "system:user:add";

    /// <summary>系统-用户-编辑。</summary>
    public const string SystemUserEdit = "system:user:edit";

    /// <summary>系统-用户-删除。</summary>
    public const string SystemUserRemove = "system:user:remove";

    /// <summary>系统-用户-恢复。</summary>
    public const string SystemUserRestore = "system:user:restore";

    /// <summary>系统-用户-导入。</summary>
    public const string SystemUserImport = "system:user:import";

    /// <summary>系统-用户-重置密码。</summary>
    public const string SystemUserResetPwd = "system:user:resetPwd";

    /// <summary>系统-用户-导出。</summary>
    public const string SystemUserExport = "system:user:export";

    /// <summary>系统-角色-导出。</summary>
    public const string SystemRoleExport = "system:role:export";

    /// <summary>系统-角色-列表。</summary>
    public const string SystemRoleList = "system:role:list";

    /// <summary>系统-角色-查询。</summary>
    public const string SystemRoleQuery = "system:role:query";

    /// <summary>系统-角色-新增。</summary>
    public const string SystemRoleAdd = "system:role:add";

    /// <summary>系统-角色-编辑。</summary>
    public const string SystemRoleEdit = "system:role:edit";

    /// <summary>系统-角色-删除。</summary>
    public const string SystemRoleRemove = "system:role:remove";

    /// <summary>系统-角色-恢复。</summary>
    public const string SystemRoleRestore = "system:role:restore";

    /// <summary>系统-岗位-列表。</summary>
    public const string SystemPostList = "system:post:list";

    /// <summary>系统-岗位-查询。</summary>
    public const string SystemPostQuery = "system:post:query";

    /// <summary>系统-岗位-新增。</summary>
    public const string SystemPostAdd = "system:post:add";

    /// <summary>系统-岗位-编辑。</summary>
    public const string SystemPostEdit = "system:post:edit";

    /// <summary>系统-岗位-删除。</summary>
    public const string SystemPostRemove = "system:post:remove";

    /// <summary>系统-岗位-恢复。</summary>
    public const string SystemPostRestore = "system:post:restore";

    /// <summary>系统-菜单-列表。</summary>
    public const string SystemMenuList = "system:menu:list";

    /// <summary>系统-菜单-查询。</summary>
    public const string SystemMenuQuery = "system:menu:query";

    /// <summary>系统-菜单-新增。</summary>
    public const string SystemMenuAdd = "system:menu:add";

    /// <summary>系统-菜单-编辑。</summary>
    public const string SystemMenuEdit = "system:menu:edit";

    /// <summary>系统-菜单-删除。</summary>
    public const string SystemMenuRemove = "system:menu:remove";

    /// <summary>系统-菜单-恢复。</summary>
    public const string SystemMenuRestore = "system:menu:restore";

    /// <summary>系统-部门-列表。</summary>
    public const string SystemDeptList = "system:dept:list";

    /// <summary>系统-部门-查询。</summary>
    public const string SystemDeptQuery = "system:dept:query";

    /// <summary>系统-部门-新增。</summary>
    public const string SystemDeptAdd = "system:dept:add";

    /// <summary>系统-部门-编辑。</summary>
    public const string SystemDeptEdit = "system:dept:edit";

    /// <summary>系统-部门-删除。</summary>
    public const string SystemDeptRemove = "system:dept:remove";

    /// <summary>系统-部门-恢复。</summary>
    public const string SystemDeptRestore = "system:dept:restore";

    /// <summary>系统-字典-列表。</summary>
    public const string SystemDictList = "system:dict:list";

    /// <summary>系统-字典-查询。</summary>
    public const string SystemDictQuery = "system:dict:query";

    /// <summary>系统-字典-新增。</summary>
    public const string SystemDictAdd = "system:dict:add";

    /// <summary>系统-字典-编辑。</summary>
    public const string SystemDictEdit = "system:dict:edit";

    /// <summary>系统-字典-删除。</summary>
    public const string SystemDictRemove = "system:dict:remove";

    /// <summary>系统-字典-恢复。</summary>
    public const string SystemDictRestore = "system:dict:restore";

    /// <summary>系统-参数配置-列表。</summary>
    public const string SystemConfigList = "system:config:list";

    /// <summary>系统-参数配置-查询。</summary>
    public const string SystemConfigQuery = "system:config:query";

    /// <summary>系统-参数配置-新增。</summary>
    public const string SystemConfigAdd = "system:config:add";

    /// <summary>系统-参数配置-编辑。</summary>
    public const string SystemConfigEdit = "system:config:edit";

    /// <summary>系统-参数配置-删除。</summary>
    public const string SystemConfigRemove = "system:config:remove";

    /// <summary>系统-参数配置-恢复。</summary>
    public const string SystemConfigRestore = "system:config:restore";

    /// <summary>系统-租户-列表。</summary>
    public const string SystemTenantList = "system:tenant:list";

    /// <summary>系统-租户-新增。</summary>
    public const string SystemTenantAdd = "system:tenant:add";

    /// <summary>系统-租户-编辑。</summary>
    public const string SystemTenantEdit = "system:tenant:edit";

    /// <summary>系统-租户-删除。</summary>
    public const string SystemTenantRemove = "system:tenant:remove";

    /// <summary>系统-租户-恢复。</summary>
    public const string SystemTenantRestore = "system:tenant:restore";

    /// <summary>系统-公告通知-列表。</summary>
    public const string SystemNoticeList = "system:notice:list";

    /// <summary>系统-公告通知-查询。</summary>
    public const string SystemNoticeQuery = "system:notice:query";

    /// <summary>系统-公告通知-新增。</summary>
    public const string SystemNoticeAdd = "system:notice:add";

    /// <summary>系统-公告通知-编辑。</summary>
    public const string SystemNoticeEdit = "system:notice:edit";

    /// <summary>系统-公告通知-删除。</summary>
    public const string SystemNoticeRemove = "system:notice:remove";

    /// <summary>系统-公告通知-恢复。</summary>
    public const string SystemNoticeRestore = "system:notice:restore";

    /// <summary>系统-文件-列表。</summary>
    public const string SystemFileList = "system:file:list";

    /// <summary>系统-文件-查询。</summary>
    public const string SystemFileQuery = "system:file:query";

    /// <summary>系统-文件-上传。</summary>
    public const string SystemFileUpload = "system:file:upload";

    /// <summary>系统-文件-下载。</summary>
    public const string SystemFileDownload = "system:file:download";

    /// <summary>系统-文件-删除。</summary>
    public const string SystemFileRemove = "system:file:remove";

    // —— test：受保护端点探针（权限链路测试专用）——

    /// <summary>测试-受保护端点-列表。</summary>
    public const string TestProtectedList = "test:protected:list";

    /// <summary>全部已声明权限（由常量反射生成，恒与常量集合一致）。</summary>
    public static readonly IReadOnlyCollection<string> All = typeof(PermissionDefinitions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();
}
