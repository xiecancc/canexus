
using System.Security.Cryptography;
using Domain.Entities;
using Domain.Services.Auth;
using Domain.Services.Rules;
using Domain.Shared.Constants;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Infrastructure.DataAccess;
/// <summary>
/// 种子数据初始化器：启动时幂等执行，按可变性分层提供基础运行数据。
/// <list type="bullet">
/// <item>代码契约类（菜单 / 按钮权限标识、字典、系统参数键）：代码常量为唯一真相，按稳定键补缺、只纠鉴权相关的 perms，不覆盖显示名 / 排序 / 图标等展示数据；写入后失效对应业务缓存；</item>
/// <item>系统锚点类（super_admin 角色、admin 用户）：按固定 Id / 稳定键查缺补齐，只插不覆盖，绝不重置用户已修改的口令；</item>
/// <item>部署配置类（初始口令、示例公告、默认部门树）：口令由运行时配置注入，生产未提供即由宿主拒绝启动，非生产随机生成且仅经控制台披露；演示数据在 Production 环境恒定不写入。</item>
/// </list>
/// <para>整体在一个事务内完成；启动期另由 <see cref="ValidateBuiltInAnchorsAsync"/> 校验系统锚点完整性。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="passwordHasher">密码哈希器（种子账号密码加密）。</param>
/// <param name="distributedLock">分布式锁（多实例并发启动时保护种子初始化的 check-then-insert 竞态）。</param>
/// <param name="bootstrap">启动引导配置（初始口令 / 演示数据开关）。</param>
/// <param name="cache">业务缓存端口（种子写入后失效参数值 / 字典数据缓存）。</param>
/// <param name="hostEnvironment">宿主环境（用于判定演示数据是否允许写入）。</param>
/// <param name="logger">日志。</param>
public sealed class SeedDataInitializer(
    ISqlSugarClient client,
    IPasswordHasher passwordHasher,
    IDistributedLock distributedLock,
    BootstrapOption bootstrap,
    ICache cache,
    IHostEnvironment hostEnvironment,
    ILogger<SeedDataInitializer> logger)
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

    /// <summary>种子初始化锁键（多实例并发启动时串行化；判重逻辑本身幂等，锁为正确性增强）。</summary>
    private const string SeedLockKey = "seed:initialize";

    /// <summary>初始化种子数据（幂等：全部按稳定键 / 固定 Id 查缺补齐，只插不覆盖，整体在一个事务内完成）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 分布式锁保护 check-then-insert 竞态：多实例并发启动时避免重复插入字典 / 配置项；
        // 未获取到锁（等待超时 / Redis 故障降级为 null）时按无锁路径继续，由判重幂等兜底
        await using var handle = await distributedLock.AcquireAsync(
            SeedLockKey, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), cancellationToken);
        if (handle is null)
        {
            logger.LogWarning("未能获取种子初始化锁（其他实例正在初始化或锁后端异常），按无锁路径继续（判重幂等兜底）");
        }

        // 演示数据仅属部署配置类：Production 环境恒定跳过，非生产由 Bootstrap:SeedDemoData 控制
        var seedDemoData = bootstrap.SeedDemoData && !hostEnvironment.IsProduction();

        await client.Ado.BeginTranAsync();
        try
        {
            await SeedDeptsAsync(seedDemoData);
            await SeedRoleAsync();
            await SeedAdminUserAsync();
            await SeedMenusAsync();
            await EnsureDictDataMenuAsync();
            await SeedDictsAsync();
            await SeedConfigsAsync();
            await SeedNoticesAsync(seedDemoData);
            await client.Ado.CommitTranAsync();
        }
        catch
        {
            await client.Ado.RollbackTranAsync();
            throw;
        }
    }

    /// <summary>
    /// 启动期校验系统锚点完整性：超管角色存在且唯一、admin 账号存在且启用、两者绑定关系存在。
    /// <para>不满足时直接抛异常阻止宿主启动，避免运行时依赖可变字符串判定超管导致越权或无法登录。</para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="InvalidOperationException">任一内置锚点缺失、重复、被停用或未绑定时抛出。</exception>
    public async Task ValidateBuiltInAnchorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 固定锚点先取到局部变量：SqlSugar 表达式解析器无法读取 private static readonly 字段
        var superAdminRoleId = SuperAdminRoleId;
        var adminUserId = AdminUserId;

        var roleCount = await client.Queryable<SysRole>()
            .CountAsync(r => r.RoleKey == RoleConstants.SuperAdminRoleKey);
        if (roleCount != 1)
        {
            throw new InvalidOperationException(
                $"内置数据锚点校验失败：超管角色 {RoleConstants.SuperAdminRoleKey} 期望唯一 1 条，实际 {roleCount} 条");
        }

        var role = await client.Queryable<SysRole>()
            .FirstAsync(r => r.Id == superAdminRoleId || r.RoleKey == RoleConstants.SuperAdminRoleKey)
            ?? throw new InvalidOperationException("内置数据锚点校验失败：未找到内置超管角色");

        var adminCount = await client.Queryable<SysUser>()
            .CountAsync(u => u.Id == adminUserId || u.UserName == UserConstants.AdminUserName);
        if (adminCount != 1)
        {
            throw new InvalidOperationException(
                $"内置数据锚点校验失败：内置账号 {UserConstants.AdminUserName} 期望唯一 1 条，实际 {adminCount} 条");
        }

        var admin = await client.Queryable<SysUser>()
            .FirstAsync(u => u.Id == adminUserId || u.UserName == UserConstants.AdminUserName);
        if (admin is null || admin.Status != CommonStatus.Enabled)
        {
            throw new InvalidOperationException("内置数据锚点校验失败：内置超管账号不存在或已停用");
        }

        var bound = await client.Queryable<SysUserRole>()
            .CountAsync(ur => ur.UserId == admin.Id && ur.RoleId == role.Id) > 0;
        if (!bound)
        {
            throw new InvalidOperationException("内置数据锚点校验失败：内置超管账号未绑定超管角色");
        }
    }

    /// <summary>种子默认部门树（演示数据）：按固定 Id 逐条补缺，只插不覆盖。</summary>
    /// <param name="seedDemoData">是否允许写入演示数据。</param>
    private async Task SeedDeptsAsync(bool seedDemoData)
    {
        if (!seedDemoData)
        {
            return;
        }

        var departments = new[]
        {
            new SysDept { Id = DeptCompanyId, ParentId = RootParentId, DeptName = "总公司", OrderNum = 1 },
            new SysDept { Id = DeptRdId, ParentId = DeptCompanyId, DeptName = "研发部", OrderNum = 1 },
            new SysDept { Id = DeptMarketId, ParentId = DeptCompanyId, DeptName = "市场部", OrderNum = 2 }
        };
        var ids = departments.Select(d => d.Id).ToArray();
        var existing = (await client.Queryable<SysDept>()
            .Where(d => ids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync()).ToHashSet();

        foreach (var dept in departments.Where(d => !existing.Contains(d.Id)))
        {
            await InsertAsync(dept);
        }
    }

    /// <summary>种子超管角色：按固定 Id / RoleKey 判重，缺失才插入，绝不覆盖既有角色定义。</summary>
    private async Task SeedRoleAsync()
    {
        var superAdminRoleId = SuperAdminRoleId;
        var exists = await client.Queryable<SysRole>()
            .CountAsync(r => r.Id == superAdminRoleId || r.RoleKey == RoleConstants.SuperAdminRoleKey) > 0;
        if (exists)
        {
            return;
        }

        await InsertAsync(new SysRole
        {
            Id = SuperAdminRoleId,
            RoleName = "超级管理员",
            RoleKey = RoleConstants.SuperAdminRoleKey,
            DataScope = DataScope.All,
            Description = "内置超管角色，放行全部权限"
        });

        logger.LogInformation("种子数据：超管角色 super_admin 已补齐");
    }

    /// <summary>
    /// 种子 admin 用户：按固定 Id / 用户名判重，缺失才插入；初始口令来自运行时配置（非生产随机生成），
    /// 默认强制首登改密（隔离测试宿主可经 Bootstrap:RequirePasswordChangeOnSeed 关闭）；
    /// 已自行改密的账号绝不重置口令，仅补齐与超管角色的绑定关系。
    /// </summary>
    private async Task SeedAdminUserAsync()
    {
        var adminUserId = AdminUserId;
        var superAdminRoleId = SuperAdminRoleId;
        var admin = await client.Queryable<SysUser>()
            .FirstAsync(u => u.Id == adminUserId || u.UserName == UserConstants.AdminUserName);

        if (admin is null)
        {
            var password = ResolveBootstrapPassword();
            await InsertAsync(new SysUser
            {
                Id = AdminUserId,
                UserName = UserConstants.AdminUserName,
                NickName = "超级管理员",
                PasswordHash = passwordHasher.Hash(password),
                MustChangePassword = bootstrap.RequirePasswordChangeOnSeed,
                DeptId = DeptCompanyId,
                Description = "内置超管账号"
            });

            // 日志只描述状态，不输出任何口令
            logger.LogInformation("种子数据：admin 账号已补齐（首登改密要求={RequireChange}）", bootstrap.RequirePasswordChangeOnSeed);
        }
        else if (bootstrap.RequirePasswordChangeOnSeed
            && passwordHasher.Verify(UserConstants.LegacySeedPassword, admin.PasswordHash))
        {
            // 存量账号仍使用历史初始口令（即从未改过密码）：一次性轮换为运行时口令并要求首登改密。
            // 已自行修改过口令的账号不做任何变更，避免每次启动重置用户凭据。
            var password = ResolveBootstrapPassword();
            if (string.Equals(password, UserConstants.LegacySeedPassword, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Bootstrap:AdminInitialPassword 不得仍为历史内置口令，请更换后重启");
            }

            admin.PasswordHash = passwordHasher.Hash(password);
            admin.MustChangePassword = true;
            await client.Updateable(admin)
                .UpdateColumns(u => new { u.PasswordHash, u.MustChangePassword })
                .ExecuteCommandAsync();
            logger.LogInformation("种子数据：内置 admin 仍使用历史初始口令，已轮换并要求首登改密");
        }
        else if (!bootstrap.RequirePasswordChangeOnSeed)
        {
            // 隔离测试宿主：显式关闭首登改密时，按运行时注入的测试口令复位内置 admin 凭据并清除改密标记，
            // 保证既有集成测试的固定登录凭据在重复启动 / 历史库复用后仍然有效；
            // Production 由启动期基线校验禁止关闭该开关，已自行改密的真实账号不受影响
            var password = ResolveBootstrapPassword();
            admin.PasswordHash = passwordHasher.Hash(password);
            admin.MustChangePassword = false;
            await client.Updateable(admin)
                .UpdateColumns(u => new { u.PasswordHash, u.MustChangePassword })
                .ExecuteCommandAsync();
            logger.LogInformation("种子数据：隔离测试宿主已复位内置 admin 测试凭据");
        }

        var role = await client.Queryable<SysRole>()
            .FirstAsync(r => r.Id == superAdminRoleId || r.RoleKey == RoleConstants.SuperAdminRoleKey);
        var roleId = role?.Id ?? superAdminRoleId;
        var userId = admin?.Id ?? AdminUserId;
        var bound = await client.Queryable<SysUserRole>()
            .CountAsync(ur => ur.UserId == userId && ur.RoleId == roleId) > 0;
        if (!bound)
        {
            await InsertAsync(new SysUserRole { UserId = userId, RoleId = roleId });
        }
    }

    /// <summary>解析内置超管的初始口令：优先取运行时配置，非生产环境未提供时生成一次性随机口令。</summary>
    /// <returns>明文初始口令（仅用于即时哈希，不持久化、不写入日志）。</returns>
    private string ResolveBootstrapPassword()
    {
        if (!string.IsNullOrWhiteSpace(bootstrap.AdminInitialPassword))
        {
            return bootstrap.AdminInitialPassword;
        }

        if (hostEnvironment.IsProduction())
        {
            throw new InvalidOperationException(
                "Bootstrap:AdminInitialPassword 未配置；Production 环境必须显式提供内置超管初始口令");
        }

        // 14 位一次性口令：固定字母前缀 + 4 位随机数字 + 8 位随机字母，保证同时含字母与数字
        var password = "Aa"
            + RandomNumberGenerator.GetString("23456789", 4)
            + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz", 8);

        // 直接写标准输出而非 ILogger：Serilog 文件 sink 会落 logs/log-*.txt，口令不得进入文件日志。
        // 仅非生产环境、仅在账号真正首次创建时输出一次。
        Console.WriteLine($"[bootstrap] 内置超管 admin 一次性初始口令：{password}（仅本次启动显示，首次登录须修改密码）");
        return password;
    }

    /// <summary>
    /// 种子完整菜单树（增量幂等）：目录 / 菜单按「菜单类型 + 名称 + 路由」判重、按钮按「父菜单 + 权限标识」判重，
    /// 缺失项才插入；同名菜单 / 按钮已存在但 component / perms 过时（如定时任务 tool:job → monitor:job）则按名称原位纠偏更新。
    /// 纠偏仅涉及组件路径与鉴权标识两类代码契约字段，不触碰显示名 / 排序 / 图标 / 可见性等展示数据。
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
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"), ("导出", "export"));
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
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(109), "岗位管理", "post", "system/post/index", "system:post:list", "post", order: 9, parentId: SeedId(100)));
        await EnsureButtonsAsync(SeedId(109), "system:post", "岗位", startId: 1081,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"));

        // 监控目录
        await EnsureMenuAsync(Menu(MenuType.Directory, SeedId(200), "监控", "monitor", null, icon: "monitor", order: 3));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(201), "操作日志", "operlog", "monitor/operlog/index", "monitor:operlog:list", "form", order: 1, parentId: SeedId(200)));
        await EnsureButtonsAsync(SeedId(201), "monitor:operlog", "操作日志", startId: 2011,
            ("查询", "query"), ("删除", "remove"), ("清空", "clean"), ("导出", "export"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(202), "登录日志", "logininfor", "monitor/logininfor/index", "monitor:logininfor:list", "logininfor", order: 2, parentId: SeedId(200)));
        await EnsureButtonsAsync(SeedId(202), "monitor:logininfor", "登录日志", startId: 2021,
            ("查询", "query"), ("删除", "remove"), ("清空", "clean"), ("导出", "export"));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(203), "缓存监控", "cache", "monitor/cache/index", "monitor/cache:list", "redis", order: 3, parentId: SeedId(200)));

        // 系统工具目录
        await EnsureMenuAsync(Menu(MenuType.Directory, SeedId(300), "系统工具", "tool", null, icon: "tool", order: 4));
        await EnsureMenuAsync(Menu(MenuType.Menu, SeedId(301), "定时任务", "job", "system/job/index", "monitor:job:list", "job", order: 1, parentId: SeedId(300)));
        await EnsureButtonsAsync(SeedId(301), "monitor:job", "定时任务", startId: 3011,
            ("查询", "query"), ("新增", "add"), ("修改", "edit"), ("删除", "remove"), ("执行", "run"), ("状态修改", "changeStatus"));

        logger.LogInformation("种子数据：菜单树已同步");
    }

    /// <summary>确保目录 / 菜单存在（按主键或「菜单类型 + 名称 + 路由」判重，缺失时插入；同名菜单 component / perms 过时则原位更新）。</summary>
    /// <param name="menu">菜单实体（携带种子固定 Id）。</param>
    private async Task EnsureMenuAsync(SysMenu menu)
    {
        var existing = await client.Queryable<SysMenu>().FirstAsync(m =>
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
            await client.Updateable(existing)
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
            var existing = await client.Queryable<SysMenu>().FirstAsync(m =>
                m.MenuType == MenuType.Button && m.ParentId == parentId && m.MenuName == menuName);
            if (existing is not null && existing.Perms != perms)
            {
                existing.Perms = perms;
                await client.Updateable(existing)
                    .UpdateColumns(m => new { m.Perms })
                    .ExecuteCommandAsync();
            }

            var exists = await client.Queryable<SysMenu>().CountAsync(m =>
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
        if (await client.Queryable<SysMenu>().CountAsync(m => m.Component == "system/dict/data") > 0)
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

        logger.LogInformation("种子数据：字典数据隐藏页菜单已补充");
    }

    /// <summary>种子字典数据（按 dictType + dictValue 幂等追加，缺失项才插入，插入后失效该类型的字典数据缓存）。</summary>
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
            logger.LogInformation("种子数据：字典数据已同步");
        }
    }

    /// <summary>种子系统参数（按 configKey 幂等追加，缺失项才插入，插入后失效参数值缓存）。</summary>
    private async Task SeedConfigsAsync()
    {
        var inserted = false;

        inserted |= await EnsureConfigAsync(ConfigConstants.InitPasswordKey, UserConstants.DefaultPassword, "用户初始密码", description: "新增用户时未显式填写密码所采用的初始密码，使用后强制首登改密");
        inserted |= await EnsureConfigAsync(ConfigConstants.CaptchaEnabledKey, "true", "登录验证码开关", description: "是否开启登录验证码校验（true 开启 / false 关闭）");

        if (inserted)
        {
            logger.LogInformation("种子数据：系统参数已同步");
        }
    }

    /// <summary>确保字典类型存在（按 dictType 判重，缺失时插入）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureDictTypeAsync(string dictType, string dictName, string? description)
    {
        if (await client.Queryable<SysDictType>().CountAsync(t => t.DictType == dictType) > 0)
        {
            return false;
        }

        await InsertAsync(new SysDictType { DictName = dictName, DictType = dictType, Description = description });
        return true;
    }

    /// <summary>确保字典数据存在（按 dictType + dictValue 判重，缺失时插入并失效该类型缓存）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureDictDataAsync(string dictType, string label, string value, TagType tagType, int orderNum)
    {
        if (await client.Queryable<SysDictData>().CountAsync(d => d.DictType == dictType && d.DictValue == value) > 0)
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
        await InvalidateCacheAsync(DictRules.DataTypeCacheKey(dictType));
        return true;
    }

    /// <summary>确保系统参数存在（按 configKey 判重，缺失时插入并失效参数值缓存）。</summary>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureConfigAsync(string configKey, string configValue, string configName, bool builtIn = true, string? description = null)
    {
        if (await client.Queryable<SysConfig>().CountAsync(c => c.ConfigKey == configKey) > 0)
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
        await InvalidateCacheAsync(ConfigRules.ValueCacheKey(configKey));
        return true;
    }

    /// <summary>种子示例通知公告（演示数据，按标题判重，缺失项才插入）。</summary>
    /// <param name="seedDemoData">是否允许写入演示数据。</param>
    private async Task SeedNoticesAsync(bool seedDemoData)
    {
        if (!seedDemoData)
        {
            return;
        }

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
            logger.LogInformation("种子数据：示例通知公告已同步");
        }
    }

    /// <summary>确保通知公告存在（按标题判重，缺失时插入）。</summary>
    /// <param name="notice">公告实体（主键自增，审计字段由数据访问层自动填充）。</param>
    /// <returns>本次是否执行了插入。</returns>
    private async Task<bool> EnsureNoticeAsync(SysNotice notice)
    {
        if (await client.Queryable<SysNotice>().CountAsync(n => n.Title == notice.Title) > 0)
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

    /// <summary>失效种子写入涉及的缓存键：缓存后端异常仅告警，不阻断数据库初始化事务。</summary>
    /// <param name="key">业务缓存键。</param>
    private async Task InvalidateCacheAsync(string key)
    {
        try
        {
            await cache.RemoveAsync(key);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "种子数据缓存失效失败：{CacheKey}（不影响初始化，后续访问将回源）", key);
        }
    }

    /// <summary>插入实体（审计字段与 Guid 主键生成由数据访问层 Aop 自动填充）。</summary>
    /// <remarks>显式设置 Id 的种子数据直接写入该主键；Guid 主键非自增，无需 OffIdentity。</remarks>
    private async Task InsertAsync<TEntity>(TEntity entity) where TEntity : DomainBase, new()
    {
        await client.Insertable(entity).ExecuteCommandAsync();
    }
}
