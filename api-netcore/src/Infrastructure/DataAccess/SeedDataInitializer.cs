namespace Infrastructure.DataAccess;

using Domain.Entities;
using Domain.Shared.Enums;
using Domain.Services.Auth;
using Microsoft.Extensions.Logging;
using SqlSugar;

/// <summary>
/// 种子数据初始化器：启动时幂等执行，提供基础运行数据。
/// <list type="bullet">
/// <item>默认部门树：总公司 / 研发部 / 市场部；</item>
/// <item>超管角色 super_admin（DataScope=All）与 admin 用户（初始密码 admin123，BCrypt 存储）；</item>
/// <item>完整菜单树（增量幂等，按名称 / path / perms 判重缺失才插入，同名菜单 / 按钮 component / perms 过时原位纠偏）：首页、系统管理（用户 / 角色 / 部门 / 菜单 / 字典 / 参数 / 通知公告 / 租户）、监控（操作日志 / 登录日志）、系统工具（定时任务，monitor:job），含按钮级权限标识；</item>
/// <item>字典（sys_normal_disable / sys_user_sex / sys_yes_no / sys_oper_type / sys_notice_type）、参数（初始密码 / 验证码开关）与示例通知公告按缺失项幂等追加。</item>
/// </list>
/// </summary>
public sealed class SeedDataInitializer
{
    /// <summary>由种子序号生成确定性占位主键（格式 00000000-0000-0000-0000-{序号:D12}，保证 36 位）。</summary>
    private static Guid SeedId(long n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static readonly Guid RootParentId = Guid.Empty;

    private static readonly Guid DeptCompanyId = SeedId(100);
    private static readonly Guid DeptRdId = SeedId(101);
    private static readonly Guid DeptMarketId = SeedId(102);

    private static readonly Guid SuperAdminRoleId = SeedId(1);
    private static readonly Guid AdminUserId = SeedId(1);

    private static readonly Guid DictMenuId = SeedId(105);

    /// <summary>字典数据隐藏页菜单固定 Id（挂在字典管理 105 下，visible=0 不进入侧边栏）。</summary>
    private static readonly Guid DictDataMenuId = SeedId(109);

    private readonly ISqlSugarClient _client;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<SeedDataInitializer> _logger;

    /// <summary>构造函数。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    /// <param name="passwordHasher">密码哈希器（种子账号密码加密）。</param>
    /// <param name="logger">日志。</param>
    public SeedDataInitializer(ISqlSugarClient client, IPasswordHasher passwordHasher, ILogger<SeedDataInitializer> logger)
    {
        _client = client;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <summary>初始化种子数据（幂等：基础数据仅当对应表为空时插入，字典与参数按缺失项追加，整体在一个事务内完成）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _client.Ado.BeginTranAsync();
        try
        {
            await SeedDeptsAsync();
            await SeedRoleAsync();
            await SeedAdminUserAsync();
            await SeedMenusAsync();
            await EnsureDictDataMenuAsync();
            await SeedDictsAsync();
            await SeedConfigsAsync();
            await SeedNoticesAsync();
            await _client.Ado.CommitTranAsync();
        }
        catch
        {
            await _client.Ado.RollbackTranAsync();
            throw;
        }
    }

    /// <summary>种子默认部门树：总公司（研发部、市场部）。</summary>
    private async Task SeedDeptsAsync()
    {
        if (await _client.Queryable<SysDept>().CountAsync() > 0)
        {
            return;
        }

        await InsertAsync(new SysDept { Id = DeptCompanyId, ParentId = RootParentId, DeptName = "总公司", OrderNum = 1 });
        await InsertAsync(new SysDept { Id = DeptRdId, ParentId = DeptCompanyId, DeptName = "研发部", OrderNum = 1 });
        await InsertAsync(new SysDept { Id = DeptMarketId, ParentId = DeptCompanyId, DeptName = "市场部", OrderNum = 2 });

        _logger.LogInformation("种子数据：默认部门树已初始化");
    }

    /// <summary>种子超管角色：super_admin（数据范围为全部）。</summary>
    private async Task SeedRoleAsync()
    {
        if (await _client.Queryable<SysRole>().CountAsync() > 0)
        {
            return;
        }

        await InsertAsync(new SysRole
        {
            Id = SuperAdminRoleId,
            RoleName = "超级管理员",
            RoleKey = "super_admin",
            DataScope = DataScope.All,
            Description = "内置超管角色，放行全部权限"
        });

        _logger.LogInformation("种子数据：超管角色 super_admin 已初始化");
    }

    /// <summary>种子 admin 用户（用户名 admin，初始密码 admin123）并绑定超管角色。</summary>
    private async Task SeedAdminUserAsync()
    {
        if (await _client.Queryable<SysUser>().CountAsync() > 0)
        {
            return;
        }

        await InsertAsync(new SysUser
        {
            Id = AdminUserId,
            UserName = "admin",
            NickName = "超级管理员",
            PasswordHash = _passwordHasher.Hash("admin123"),
            DeptId = DeptCompanyId,
            Description = "内置超管账号"
        });

        await InsertAsync(new SysUserRole { UserId = AdminUserId, RoleId = SuperAdminRoleId });

        _logger.LogInformation("种子数据：admin 账号已初始化（初始密码 admin123）");
    }

    /// <summary>
    /// 种子完整菜单树（增量幂等）：目录 / 菜单按「菜单类型 + 名称 + 路由」判重、按钮按「父菜单 + 权限标识」判重，
    /// 缺失项才插入；同名菜单 / 按钮已存在但 component / perms 过时（如定时任务 tool:job → monitor:job）则按名称原位纠偏更新。
    /// 超管按 roleKey 放行全部权限，无需角色-菜单关联。
    /// </summary>
    private async Task SeedMenusAsync()
    {
        // 首页（顶级菜单）
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(1), "首页", "/dashboard", "dashboard/index", perms: null, icon: "dashboard", order: 1, keepAlive: true));

        // 系统管理目录
        await EnsureMenuAsync(Menu(MenuType.Directory, SeedId(100), "系统管理", "system", null, icon: "system", order: 2));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(101), "用户管理", "user", "system/user/index", "system:user:list", "user", order: 1, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(101), "system:user", "用户", startId: 1001,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"), ("重置密码", "resetPwd"), ("导入", "import"), ("导出", "export"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(102), "角色管理", "role", "system/role/index", "system:role:list", "peoples", order: 2, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(102), "system:role", "角色", startId: 1011,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(103), "部门管理", "dept", "system/dept/index", "system:dept:list", "tree", order: 3, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(103), "system:dept", "部门", startId: 1021,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(104), "菜单管理", "menu", "system/menu/index", "system:menu:list", "tree-table", order: 4, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(104), "system:menu", "菜单", startId: 1031,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(105), "字典管理", "dict", "system/dict/index", "system:dict:list", "dict", order: 5, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(105), "system:dict", "字典", startId: 1041,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(106), "参数配置", "config", "system/config/index", "system:config:list", "edit", order: 6, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(106), "system:config", "参数", startId: 1051,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(107), "通知公告", "notice", "system/notice/index", "system:notice:list", "message", order: 7, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(107), "system:notice", "公告", startId: 1061,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(108), "租户管理", "tenant", "system/tenant/index", "system:tenant:list", "tenant", order: 8, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(108), "system:tenant", "租户", startId: 1071,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));

        // 监控目录
        await EnsureMenuAsync(Menu(MenuType.Directory, SeedId(200), "监控", "monitor", null, icon: "monitor", order: 3));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(201), "操作日志", "operlog", "monitor/operlog/index", "monitor:operlog:list", "form", order: 1, parentId: SeedId(200)));
        await EnsureButtonsAsync(SeedId(201), "monitor:operlog", "操作日志", startId: 2011,
            ("查询", "query"), ("删除", "remove"), ("清空", "clean"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(202), "登录日志", "logininfor", "monitor/logininfor/index", "monitor:logininfor:list", "logininfor", order: 2, parentId: SeedId(200)));
        await EnsureButtonsAsync(SeedId(202), "monitor:logininfor", "登录日志", startId: 2021,
            ("查询", "query"), ("删除", "remove"), ("清空", "clean"));

        // 系统工具目录
        await EnsureMenuAsync(Menu(MenuType.Directory, SeedId(300), "系统工具", "tool", null, icon: "tool", order: 4));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(301), "定时任务", "job", "system/job/index", "monitor:job:list", "job", order: 1, parentId: SeedId(300)));
        await EnsureButtonsAsync(SeedId(301), "monitor:job", "定时任务", startId: 3011,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"), ("执行", "run"), ("状态修改", "changeStatus"));

        _logger.LogInformation("种子数据：菜单树已同步");
    }

    /// <summary>确保目录 / 菜单存在（按主键或「菜单类型 + 名称 + 路由」判重，缺失时插入；同名菜单 component / perms 过时则原位更新）。</summary>
    /// <param name="menu">菜单实体（携带种子固定 Id）。</param>
    private async Task EnsureMenuAsync(SysMenu menu)
    {
        var existing = await _client.Queryable<SysMenu>().FirstAsync(m =>
            m.Id == menu.Id
            || (m.MenuType == menu.MenuType && m.MenuName == menu.MenuName && m.Path == menu.Path));
        if (existing is null)
        {
            await InsertAsync(menu);
            return;
        }

        // 原位纠偏：同名菜单的 component / perms 过时（如定时任务 tool/job → system/job）时按名称更新，避免旧标识残留
        if (existing.MenuName == menu.MenuName
            && (existing.Component != menu.Component || existing.Perms != menu.Perms))
        {
            existing.Component = menu.Component;
            existing.Perms = menu.Perms;
            await _client.Updateable(existing)
                .UpdateColumns(m => new { m.Component, m.Perms })
                .ExecuteCommandAsync();
        }
    }

    /// <summary>在指定菜单下批量确保按钮级权限存在（按父菜单 + 权限标识判重，缺失项才插入；同名按钮 perms 过时则原位更新）。</summary>
    /// <param name="parentId">父菜单 Id。</param>
    /// <param name="permsPrefix">权限前缀（如 system:user）。</param>
    /// <param name="namePrefix">按钮名称前缀。</param>
    /// <param name="startId">起始种子序号。</param>
    /// <param name="buttons">(名称, 动作) 列表。</param>
    private async Task EnsureButtonsAsync(Guid parentId, string permsPrefix, string namePrefix, long startId, params (string Name, string Action)[] buttons)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var perms = $"{permsPrefix}:{buttons[i].Action}";
            var menuName = namePrefix + buttons[i].Name;
            var buttonId = SeedId(startId + i);

            // 原位纠偏：同名按钮的 perms 前缀过时（如 tool:job → monitor:job）时按「父菜单 + 按钮名称」更新
        var existing = await _client.Queryable<SysMenu>().FirstAsync(m =>
            m.MenuType == MenuType.Button && m.ParentId == parentId && m.MenuName == menuName);
            if (existing is not null && existing.Perms != perms)
            {
                existing.Perms = perms;
                await _client.Updateable(existing)
                    .UpdateColumns(m => new { m.Perms })
                    .ExecuteCommandAsync();
            }

            var exists = await _client.Queryable<SysMenu>().CountAsync(m =>
                m.Id == buttonId
                || (m.MenuType == MenuType.Button && m.ParentId == parentId && m.Perms == perms)) > 0;
            if (exists)
            {
                continue;
            }

            await InsertAsync(Menu(
                MenuType.Button,
                buttonId,
                menuName,
                "#",
                null,
                perms,
                icon: null,
                order: i + 1,
                parentId: parentId));
        }
    }

    /// <summary>补充字典数据隐藏页菜单（system/dict/data，visible=0；历史库也幂等追加）。</summary>
    private async Task EnsureDictDataMenuAsync()
    {
        if (await _client.Queryable<SysMenu>().CountAsync(m => m.Component == "system/dict/data") > 0)
        {
            return;
        }

        await InsertAsync(Menu(
            MenuType.Menu,
            DictDataMenuId,
            "字典数据",
            "dict/data/:dictId",
            "system/dict/data",
            perms: "system:dict:list",
            icon: "dict",
            order: 1,
            parentId: DictMenuId,
            visible: MenuVisible.Hide));

        _logger.LogInformation("种子数据：字典数据隐藏页菜单已补充");
    }

    /// <summary>种子字典数据（按 dictType + dictValue 幂等追加，缺失项才插入，不破坏既有数据）。</summary>
    private async Task SeedDictsAsync()
    {
        var inserted = false;

        inserted |= await EnsureDictTypeAsync("sys_normal_disable", "系统状态", "系统启用 / 停用状态列表");
        inserted |= await EnsureDictDataAsync("sys_normal_disable", "启用", "1", TagType.Primary, 1);
        inserted |= await EnsureDictDataAsync("sys_normal_disable", "停用", "0", TagType.Danger, 2);

        inserted |= await EnsureDictTypeAsync("sys_user_sex", "用户性别", "用户性别列表");
        inserted |= await EnsureDictDataAsync("sys_user_sex", "男", "1", TagType.Primary, 1);
        inserted |= await EnsureDictDataAsync("sys_user_sex", "女", "2", TagType.Success, 2);
        inserted |= await EnsureDictDataAsync("sys_user_sex", "未知", "3", TagType.Info, 3);

        inserted |= await EnsureDictTypeAsync("sys_yes_no", "系统是否", "系统是否列表");
        inserted |= await EnsureDictDataAsync("sys_yes_no", "是", "Y", TagType.Primary, 1);
        inserted |= await EnsureDictDataAsync("sys_yes_no", "否", "N", TagType.Info, 2);

        inserted |= await EnsureDictTypeAsync("sys_oper_type", "操作类型", "操作日志业务类型列表");
        inserted |= await EnsureDictDataAsync("sys_oper_type", "新增", "1", TagType.Primary, 1);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "修改", "2", TagType.Info, 2);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "删除", "3", TagType.Danger, 3);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "授权", "4", TagType.Warning, 4);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "导出", "5", TagType.Info, 5);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "导入", "6", TagType.Info, 6);
        inserted |= await EnsureDictDataAsync("sys_oper_type", "清空", "8", TagType.Danger, 7);

        inserted |= await EnsureDictTypeAsync("sys_notice_type", "通知公告类型", "通知公告类型列表");
        inserted |= await EnsureDictDataAsync("sys_notice_type", "通知", "1", TagType.Primary, 1);
        inserted |= await EnsureDictDataAsync("sys_notice_type", "公告", "2", TagType.Info, 2);

        if (inserted)
        {
            _logger.LogInformation("种子数据：字典数据已同步");
        }
    }

    /// <summary>种子系统参数（按 configKey 幂等追加，缺失项才插入）。</summary>
    private async Task SeedConfigsAsync()
    {
        var inserted = false;

        inserted |= await EnsureConfigAsync("sys.user.initPassword", "123456", "用户初始密码", description: "新增用户时的缺省初始密码");
        inserted |= await EnsureConfigAsync("sys.account.captchaEnabled", "true", "登录验证码开关", description: "是否开启登录验证码校验（true 开启 / false 关闭）");

        if (inserted)
        {
            _logger.LogInformation("种子数据：系统参数已同步");
        }
    }

    /// <summary>确保字典类型存在（按 dictType 判重，缺失时插入）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureDictTypeAsync(string dictType, string dictName, string? description)
    {
        if (await _client.Queryable<SysDictType>().CountAsync(t => t.DictType == dictType) > 0)
        {
            return false;
        }

        await InsertAsync(new SysDictType { DictName = dictName, DictType = dictType, Description = description });
        return true;
    }

    /// <summary>确保字典数据存在（按 dictType + dictValue 判重，缺失时插入）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureDictDataAsync(string dictType, string label, string value, TagType tagType, int orderNum)
    {
        if (await _client.Queryable<SysDictData>().CountAsync(d => d.DictType == dictType && d.DictValue == value) > 0)
        {
            return false;
        }

        await InsertAsync(new SysDictData
        {
            DictType = dictType,
            DictLabel = label,
            DictValue = value,
            TagType = tagType,
            OrderNum = orderNum,
            Status = CommonStatus.Enabled
        });
        return true;
    }

    /// <summary>确保系统参数存在（按 configKey 判重，缺失时插入）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureConfigAsync(string configKey, string configValue, string configName, bool builtIn = true, string? description = null)
    {
        if (await _client.Queryable<SysConfig>().CountAsync(c => c.ConfigKey == configKey) > 0)
        {
            return false;
        }

        await InsertAsync(new SysConfig
        {
            ConfigName = configName,
            ConfigKey = configKey,
            ConfigValue = configValue,
            BuiltIn = builtIn,
            Description = description
        });
        return true;
    }

    /// <summary>种子示例通知公告（按标题判重，缺失项才插入）：一条上线通知（类型 1，正常）+ 一条维护公告（类型 2，关闭）。</summary>
    private async Task SeedNoticesAsync()
    {
        var inserted = false;

        inserted |= await EnsureNoticeAsync(new SysNotice
        {
            Title = "系统上线通知",
            NoticeType = NoticeType.Notification,
            Content = "平台已完成部署并正式上线，各功能模块均可正常使用，如遇问题请联系系统管理员。",
            Status = CommonStatus.Enabled,
            Description = "内置示例公告，可在通知公告页维护"
        });
        inserted |= await EnsureNoticeAsync(new SysNotice
        {
            Title = "系统维护公告",
            NoticeType = NoticeType.Announcement,
            Content = "平台将于近期进行例行维护，期间可能出现短暂不可用，请提前保存工作内容。",
            Status = CommonStatus.Disabled,
            Description = "内置示例公告，默认关闭，开启后展示于首页公告卡片"
        });

        if (inserted)
        {
            _logger.LogInformation("种子数据：示例通知公告已同步");
        }
    }

    /// <summary>确保通知公告存在（按标题判重，缺失时插入）。</summary>
    /// <param name="notice">公告实体（主键自增，审计字段由数据访问层自动填充）。</param>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureNoticeAsync(SysNotice notice)
    {
        if (await _client.Queryable<SysNotice>().CountAsync(n => n.Title == notice.Title) > 0)
        {
            return false;
        }

        await InsertAsync(notice);
        return true;
    }

    /// <summary>构造菜单实体。</summary>
    /// <param name="menuType">菜单类型。</param>
    /// <param name="id">显式主键（种子固定 Id，便于父子引用）。</param>
    /// <param name="name">菜单名称。</param>
    /// <param name="path">路由地址。</param>
    /// <param name="component">组件路径。</param>
    /// <param name="perms">权限标识。</param>
    /// <param name="icon">图标。</param>
    /// <param name="order">显示顺序。</param>
    /// <param name="parentId">父菜单 Id。</param>
    /// <param name="keepAlive">页面是否缓存。</param>
    /// <param name="visible">是否显示（隐藏页路由仍存在但不进入侧边栏）。</param>
    /// <returns>菜单实体。</returns>
    private static SysMenu Menu(
        MenuType menuType,
        Guid id,
        string name,
        string path,
        string? component,
        string? perms = null,
        string? icon = null,
        int order = 1,
        Guid parentId = default,
        bool keepAlive = false,
        MenuVisible visible = MenuVisible.Show) => new()
    {
        Id = id,
        ParentId = parentId,
        MenuType = menuType,
        MenuName = name,
        Path = path,
        Component = component,
        Perms = perms,
        Icon = icon,
        OrderNum = order,
        Visible = visible,
        KeepAlive = keepAlive,
        Status = CommonStatus.Enabled
    };

    /// <summary>插入实体（审计字段与 Guid 主键生成由数据访问层 Aop 自动填充）。</summary>
    /// <remarks>显式设置 Id 的种子数据直接写入该主键；Guid 主键非自增，无需 OffIdentity。</remarks>
    private async Task InsertAsync<TEntity>(TEntity entity) where TEntity : DomainBase, new()
    {
        await _client.Insertable(entity).ExecuteCommandAsync();
    }
}
