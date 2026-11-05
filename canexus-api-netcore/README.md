# canexus-api-netcore — 后端服务

基于 .NET 10 的中后台管理系统后端，采用经典分层架构（API / Application / Domain / Infrastructure），内置 RBAC 权限、多租户、乐观并发、定时任务调度与完整审计能力。数据库结构由 SqlSugar CodeFirst 自动创建并填充种子数据，开箱即用。

## 技术路线

| 分类 | 技术 | 版本 |
| --- | --- | --- |
| 运行时 | .NET | 10.0 |
| ORM | SqlSugarCore（CodeFirst / 仓储 / 全局过滤器） | 5.1.4.221 |
| 数据库 | MySQL（驱动 MySqlConnector） | MySqlConnector 2.6.2 |
| 认证 | JWT（JwtBearer + System.IdentityModel.Tokens.Jwt） | 10.0.11 / 8.5.0 |
| 密码 | BCrypt.Net-Next 哈希 | 4.0.3 |
| 对象映射 | AutoMapper（锁定 14.x：15.0.0 起转 Lucky Penny 商业/RPL 双许可，14.0.0 为最后的 Apache-2.0 版本） | 14.0.0 |
| CQRS | MediatR（命令 / 查询分发 + Pipeline Behaviors） | 12.4.1 |
| 输入验证 | FluentValidation（+ DependencyInjectionExtensions） | 12.1.1 |
| 任务调度 | Quartz（+ Extensions.DependencyInjection / Hosting，实现在基础设施层） | 3.22.9 |
| 日志 | Serilog.AspNetCore（控制台 + 文件） | 10.0.0 |
| API 文档 | Microsoft.AspNetCore.OpenApi（/openapi/v1.json） | 10.0.11 |
| Excel 导出 | MiniExcel | 1.46.0 |

## 功能模块

| 模块 | 说明 | 主要接口 |
| --- | --- | --- |
| 认证授权 | 登录（验证码 + 失败锁定）、刷新令牌、JWT 签发 | `AuthController` |
| 用户管理 | 用户 CRUD、分配角色、重置密码、启停状态 | `SysUserController` |
| 角色管理 | 角色 CRUD、菜单授权、数据范围（本部门 / 部门及以下等） | `SysRoleController` |
| 部门管理 | 树形部门 CRUD、祖级路径维护、环检测 | `SysDeptController` |
| 菜单管理 | 目录 / 菜单 / 按钮三级树、动态路由 | `SysMenuController` |
| 字典管理 | 字典类型 + 字典数据 | `SysDictTypeController` / `SysDictDataController` |
| 参数配置 | 系统参数内置键保护 | `SysConfigController` |
| 通知公告 | 公告 CRUD、最新公告 | `SysNoticeController` |
| 租户管理 | 租户 CRUD、占用校验、多租户数据隔离（可开关） | `SysTenantController` |
| 定时任务 | Quartz 作业 CRUD、启停、立即执行、执行日志 | `SysJobController` / `SysJobLogController` |
| 系统监控 | 操作日志 / 登录日志查询、清空、Excel 导出 | `SysOperLogController` / `SysLoginLogController` |

## 目录结构

```
canexus-api-netcore/
├── api.slnx                        # 解决清单文件
├── src/
│   ├── API/                        # 宿主层：Controller、过滤器/特性、中间件、Program、appsettings
│   │   ├── Controllers/            # REST 接口（{id:guid} 路由约束）
│   │   ├── Authorization/          # RequirePermission 权限特性
│   │   ├── Filters/                # 权限过滤器、操作日志过滤器
│   │   ├── Logging/                # OperLog 特性与脱敏工具
│   │   ├── Middlewares/            # 全局异常、租户解析
│   │   └── appsettings*.json       # 连接串、Jwt、多租户开关、Serilog
│   ├── Application/                # 应用层：CQRS 处理器实现（MediatR）+ 权限校验 + 数据范围
│   │   ├── Abstractions/           # RequestHandler / CommandHandler / QueryHandler 处理器基类（封装 MediatR 适配）
│   │   ├── Commands/               # 写操作处理器：{Entity}Commands.cs（同实体处理器合并单文件）
│   │   ├── Queries/                # 读操作处理器：{Entity}Queries.cs（同实体处理器合并单文件）
│   │   ├── Validators/             # FluentValidation 验证器：{Entity}Validators.cs（按实体合并单文件）
│   │   ├── Behaviors/              # Pipeline Behaviors：验证 / 日志 / 性能监控
│   │   ├── Mappings/               # AutoMapper Profile（实体 → 视图对象）
│   │   ├── Permissions/            # 权限缓存校验（IPermissionChecker）
│   │   ├── Scopes/                 # 数据范围过滤（DataScopeFilter）
│   │   └── Events/                 # 领域事件：MediatR 发布器 + 通知信封 + 领域监听器（权限缓存 / 字典缓存 / 配置缓存失效、事件审计日志）
│   ├── Application.Contracts/      # 契约层：CQRS 请求定义 + 请求/响应 DTO、Result 统一响应
│   │   ├── Abstractions/           # IRequest / ICommand / IQuery 请求标记接口层次（契约与实现分离）
│   │   ├── Commands/               # 写操作请求定义：{Entity}Commands.cs（仅命令 record，不含实现）
│   │   └── Queries/                # 读操作请求定义：{Entity}Queries.cs（仅查询 record，不含实现）
│   ├── Domain/                     # 领域层：实体、仓储接口、领域服务端口
│   │   ├── Entities/               # DomainBase（创建审计 + 租户归属，无主键）/ AggregateBase / AggregateTreeBase 基类 + 17 个 Sys* 实体（关联表继承 DomainBase，业务字段联合主键）
│   │   ├── Events/                 # 领域事件：DomainEvent 通用事件模型 + 按领域划分的事件文件（用户 / 角色 / 菜单 / 部门 / 字典 / 配置 / 通知 / 租户 / 作业 / 日志）
│   │   ├── Repositories/           # IDomainRepository（查询 + 插入 + 物理删除）/ IAggregateRepository（聚合专属：乐观并发更新、软删除、恢复）/ IAggregateTreeRepository
│   │   ├── Services/               # 跨层端口接口：IJobScheduleManager（调度）、ICaptchaService（验证码）、ITokenService、IPasswordHasher、IUnitOfWork、UserContext
│   │   │   └── Rules/              # 纯逻辑领域校验规则：MenuRules / DictRules / ConfigRules / JobRules / NoticeRules / LogQueryBounds
│   ├── Domain.Shared/              # 共享内核：枚举、错误码、BusinessException、分页
│   ├── Infrastructure/             # 基础设施：SqlSugar 仓储实现、JWT、Quartz 调度、租户、当前用户
│   │   ├── DataAccess/             # SqlSugarDomainRepository（三仓储实现：领域 / 聚合 / 树形聚合）、DbInitializer、种子数据
│   │   ├── Scheduling/             # Quartz 实现：JobScheduleManager、执行包装任务、示例任务
│   │   ├── Repositories/           # 日志专属仓储（物理清空/删除）
│   │   ├── Auth/                   # JwtTokenService、BCryptPasswordHasher 实现（端口接口定义于 Domain/Services）
│   │   ├── Users/                  # HttpContextUserContext 实现（当前用户上下文唯一实现）
│   │   └── Tenants/                # TenantContext 租户上下文实现
│   └── Infrastructure.Shared/      # 基础设施共享：双层缓存（IAppCache 端口 + HybridCache 实现 + TTL 档位）、验证码、数据过滤开关、UA 之外的共享实现
└── test/
    ├── Domain.Tests/               # 领域层单测（基类相等性等）
    ├── Infrastructure.Tests/       # 数据访问集成测试（真实 MySQL：过滤器、乐观锁、CodeFirst）
    ├── Application.Tests/          # 应用服务单测
    └── API.Tests/                  # 全链路 HTTP 集成测试（WebApplicationFactory）+ 宿主层单测
```

## 领域模型约定

- **统一标识符**：所有实体主键 / 外键为 GUID（36 位字符串），由数据访问层在插入时自动生成
- **基类体系**：`DomainBase`（创建审计 CreatedAt / CreatedBy（Guid）+ 租户归属 TenantId；无主键，预留领域事件）→ `AggregateBase`（单主键 Id + 乐观并发 Version（Guid 令牌）+ 软删除 + 更新审计 UpdatedAt / UpdatedBy（Guid））→ `AggregateTreeBase<T>`（树形结构，ParentId / Parent / Children）；关联表直接继承 `DomainBase`
- **审计字段**：`CreatedAt / CreatedBy / UpdatedAt / UpdatedBy` 由框架自动填充（审计人为用户 Id，无登录上下文时为空 Guid），业务代码无需赋值
- **乐观并发**：`Version` 为 Guid 版本令牌（`IsEnableUpdateVersionValidation = true`），更新走 SqlSugar `ExecuteCommandWithOptLock` 框架级校验（自动以旧值作更新条件并轮换新 Guid），冲突抛出业务异常（错误码 `40005`）
- **软删除**：删除默认为墓碑标记，查询自动过滤；`RestoreAsync` 可恢复
- **仓储三分**：`IDomainRepository`（查询统计 + 投影查询（Select&lt;TDto&gt; 按属性名自动映射返回指定类型）+ 插入 + 按条件删除，实体表 / 关联表公共；可空条件参数配合 WhereIF 按需拼接）/ `IAggregateRepository`（聚合专属：乐观并发更新、软删除与恢复（按主键 / 集合 / 条件全族单条 SQL 按列更新——仓储兜底填充更新人 / 更新时间并轮换版本令牌，防止墓碑被旧令牌复活））/ `IAggregateTreeRepository`（树形查询）。接口只定义契约（无 new 约束），`new()` 仅出现在 SqlSugar 实现类上
- **关联表实体**：SysUserRole / SysRoleMenu / SysRoleDept 继承 `DomainBase`（非聚合根）：无独立主键，以业务字段联合主键唯一定位；具备创建审计与租户隔离（全局租户过滤器与聚合根一致），无乐观并发与更新审计语义，删除为物理删除
- **CQRS**：请求定义（record 实现 IRequest / ICommand / IQuery 接口层次）位于 Application.Contracts；处理器继承 RequestHandler → CommandHandler / QueryHandler 基类（Application/Abstractions，封装 MediatR 适配，子类实现 HandleAsync）；验证器独立于 Validators 目录，横切关注点（验证 / 日志 / 性能）由 Pipeline Behaviors 统一处理
- **DTO 继承体系**：`Application.Contracts/Common` 提供与领域对齐的基类——请求侧 `StatusRequest` / `StatusDescriptionRequest`（对齐 Status / Description），视图侧 `EntityVO` / `StatusVO`（对齐 Id / CreatedAt / Status）；各模块以 `XxxWriteRequest` / `XxxBaseVO` 抽取 Create-Update 与 List-Detail 共同字段，派生类零重复定义
- **校验分层**：请求级校验统一收敛于 **Validators 模块**（FluentValidation，`Application/Validators/`）：公共规则以扩展方法沉淀于 `Validators/Common/ValidationRules`（NotBlank / TrimmedMaxLength / Status / InRange / ConfigKey）；模块验证器按「请求级验证器（泛型，约束到 Write 基类 DTO）+ 命令验证器（SetValidator 委托）」组织，模块错误码经 Custom 规则透传；实体 setter 保留单属性不变量（Guard）作为领域最后防线
- **DTO → 实体映射**：AutoMapper **按实体拆分独立 Profile**（`Mappings/SysUserProfile` / `SysRoleProfile` / `SysMenuProfile` / `SysDeptProfile` / `SysDictProfile` / `SysConfigProfile` / `SysJobProfile` / `SysNoticeProfile` / `SysTenantProfile` / `SysLogProfile`，公共约定（字符串 trim、PagedResult 开放泛型映射）沉淀于 `CommonProfile`）；14 组 Create/Update 请求 → 实体映射 + 全局字符串值转换器自动 trim；实体 setter 同步归一化，双保险
- **富模型（setter 模式）**：实体属性 setter 内强制单属性不变量——trim 归一化（Domain.Shared/Validation/Guard）、非空、取值范围（状态 0/1、标签样式 1~5、数据范围 1~5、菜单类型等，经模块 Rules Ensure 方法）；跨属性耦合规则（如菜单类型 ↔ path/component/perms）由 `Create(...)` / `Apply(...)` 调用 Rules 校验，非法状态无法构造或逃逸
- **仓储分层**：`IDomainRepository`（查询 + 插入 + 按条件删除：关联表为物理删除，聚合实现重写为软删除）→ `IAggregateRepository`（软删除 + 乐观并发更新 + 恢复）→ `IAggregateTreeRepository`（树查询）→ `IAncestorsTreeRepository`（**后代 Ancestors 批量刷新**，仅限实现 IAncestorsTree 的树形聚合如部门）；专属仓储：三张日志表（含 HardDeleteAsync） + `ISysRefreshTokenRepository`（GetActiveByTokenAsync / RevokeAllForUserAsync / RemoveExpiredAsync）
- **工作单元**：`IUnitOfWork` 仅暴露 `RunInTransactionAsync`（委托成功自动提交、异常自动回滚并原样透出），实现基于 SqlSugar `Ado.UseTranAsync`，无需显式 Begin / Commit / Rollback
- **端口归位**：领域技术端口（ITokenService / IPasswordHasher / IUnitOfWork / UserContext / ICaptchaService 等）定义于 Domain/Services，实现在 Infrastructure(.Shared)；缓存端口 `IAppCache` 与 TTL 档位定义于 Infrastructure.Shared/Caching（支持层共享设施），应用层经 DI 消费，不感知缓存技术细节
- **领域事件**：根基类 `DomainEvent` 仅承载共有元数据（事件名称 EventName = 短类名，即事件类型的唯一标识；发生时间 OccurredAt），**事件类型由事件类本身表达**，无独立类型字符串与弱类型载荷。泛型基类 `DomainEvent<TDomain>`（TDomain : DomainBase）携带事件关联的领域实体（Domain 属性）。**聚合级事件基类族按变更类型分层**：`AggregateCreatedEvent<TAggregate>`（Domain = 创建后状态）/ `AggregateUpdatedEvent<TAggregate>`（Domain = 变更后状态，Before = 变更前快照，发布点经 DomainBase.Snapshot 浅拷贝捕获）/ `AggregateDeletedEvent<TAggregate>`（Domain = 删除前状态）/ `AggregateRestoredEvent<TAggregate>`（恢复，预留）；无实体状态的事件（UserPasswordResetEvent，密码哈希敏感）与聚合根可空的日志事件（LogDeletedEvent）直接继承 DomainEvent。**事件文件按所属领域划分**（UserEvents / RoleEvents / MenuEvents / DeptEvents / DictEvents / ConfigEvents / NoticeEvents / TenantEvents / JobEvents / LogEvents），每领域按创建 / 修改 / 删除拆分具体事件类（如 UserCreatedEvent / UserUpdatedEvent / UserDeletedEvent）。发布端口 `IDomainEventPublisher` 定义于 Domain/Services/Events；全部业务写命令处理器在变更成功后的关键节点发布对应事件。应用层经 MediatR 非泛型通知信封（DomainEventNotification）进程内发布；监听器继承泛型基类 `DomainEventHandler<TEvent>`（屏蔽类型过滤守卫，TEvent 传 DomainEvent 即全量订阅）或直接实现 INotificationHandler 模式匹配（多类型监听器受类单继承限制）——**业务逻辑与响应逻辑解耦**，新增监听需求只需追加监听器实现，由 MediatR 扫描自动注册。内置监听器：权限缓存失效（用户事件→单用户，角色 / 菜单事件→全量）、字典缓存失效（类型迁移经 Before.DictType 比对新旧双失效）、配置缓存失效（键名变更经 Before.ConfigKey 比对新旧双失效）、事件审计（DomainEventAuditHandler，通用兜底记录事件名称与发生时间，不落实体状态以规避敏感数据）。**监听器命名约定**：`<响应关切><名词化响应动作>Handler`（如 PermissionCacheInvalidationHandler / DomainEventAuditHandler），统一继承泛型基类（单类型）或直接实现 INotificationHandler（多类型）。**可靠性（P1-A2 已落地）**：领域事件经事务 Outbox 发布——发布器在业务事务内将事件序列化持久化到 sys_outbox_message 并加入请求作用域待投递队列，事务提交成功后由 TransactionBehavior 同步投递（失败仅记录日志、不阻断业务响应），后台 worker（Outbox:Enabled）按 Outbox:PollIntervalSeconds 轮询兜底崩溃恢复与重试，超过 Outbox:MaxAttempts 置为失败状态保留排查；监听器异常隔离由 MediatR IsolatedNotificationPublisher 保证（单监听器异常不影响其他监听器与主流程）。Outbox 载荷经 System.Text.Json 序列化，敏感字段（如 SysUser.PasswordHash）已 [JsonIgnore] 排除，事件载荷不落敏感数据。评估后不发布事件的写操作：登录 / 登出 / 刷新令牌（认证基础设施流程）、立即执行一次任务（不改变聚合数据）、操作 / 登录日志写入（OperLog 过滤器承载审计职责）
- **领域规则归位**：纯逻辑校验规则（MenuRules / DictRules / ConfigRules / JobRules / NoticeRules / LogQueryBounds）位于 Domain/Services/Rules，供处理器与 Domain.Tests 复用；树形装配统一由 `AggregateTreeBase<T>.BuildTree` 承接（同级按 OrderNum 升序再按 Id、孤儿节点挂根级），树查询处理器过滤后建树并经 AutoMapper 递归映射为契约 VO
- **双层缓存**：基于 HybridCache 的 L1 进程内存（短过期）+ L2 Redis（长过期，`HybridCache:RedisEnabled` 开启后生效）缓存，按 TTL 档位（Hot / Medium / Cold，可配置）自动调整两级过期策略；应用层统一经 `IAppCache` 端口读写与失效，不感知缓存技术


## 安全边界（P0 已落地）

- **多租户访问校验（S1 + P1-A1）**：登录与业务接口均校验租户归属与状态——TenantResolutionMiddleware 解析 X-Tenant-Id 头并核对 JWT 租户声明，TenantAuthorizationMiddleware 经 ITenantAccessValidator 校验：跨租户访问 40301、平台/匿名带头 400801、租户停用 400805、租户到期 400806；登录链路（P1-A1）在账号状态校验后执行同一租户校验，并经独立连接记录登录失败日志（不受事务回滚影响）。MultiTenancy:Enabled=false 时校验全部放行
- **刷新令牌哈希存储（S2）**：刷新令牌经 RefreshTokenHasher 以 SHA-256 哈希持久化（数据库不存明文），明文仅存在于内存与传输过程；存量明文记录自动失效，需重新登录
- **初始密码强制改密（S4）**：新建 / 重置密码用户的初始密码统一为 UserConstants.DefaultPassword = Aa123456（满足密码策略）；首次登录返回 mustChangePassword=true，MustChangePasswordMiddleware 强制改密（403/40302，改密 / 登出等白名单端点豁免），改密接口 POST /api/auth/change-password
- **上传上限对齐（S3）**：Kestrel MaxRequestBodySize 全局对齐业务上限（FileStorage:MaxSizeMB + 1MB，覆盖 multipart 边界开销），超限在线级返回 413/401203，不依赖端点级 [RequestSizeLimit] 单独声明
- **权限注册表（B4）**：控制器权限声明统一引用 Domain.Shared/Permissions/PermissionDefinitions 常量（命名 模块:动作:资源），PermissionsStartupValidator 在启动期校验全部引用均已注册——新增权限未登记将编译 / 启动双失败，杜绝权限串漂移
## 环境要求

- .NET SDK 10.0
- MySQL 8.x（本地默认 `localhost:3306`，账号 `root/***REMOVED***`，或通过配置修改）

## 构建约束与代码风格

- 各项目 `.csproj`（src 与 test 共 11 个项目）统一声明：**`TreatWarningsAsErrors`（编译警告即错误）** 与 **`EnforceCodeStyleInBuild`（构建期强制 `.editorconfig` 代码风格）**——不使用 `Directory.Build.props`，配置全部落位在项目文件内
- 代码风格基线由 `.editorconfig` 定义：风格类规则统一 **warning 级**，配合各项目的 `TreatWarningsAsErrors`，编译警告与代码风格违规在构建期均按**错误**处理（不合规代码无法通过构建）；格式统一经 `dotnet format api.slnx` 执行，各配置项的作用以 `.editorconfig` 内注释为准（该文件是规则与说明的唯一载体）
- **变量声明优先 `var`**：内置类型 / 类型明显 / 其余场景均用 `var`（`csharp_style_var_*` 三项均为 `true`）
- **类优先主构造函数 + `get` 只读属性**：依赖与构造参数经 C# 12 主构造函数承载，转存为 `{ get; }` 只读自动属性，避免传统构造函数字段回填样板（`csharp_style_prefer_primary_constructors`）
- 提交 `.cs` 文件时，仓库级 pre-commit 钩子（husky + lint-staged，见 `canexus-admin-vue/README.md`）会自动对其运行 `dotnet format --include`；推送前执行 `dotnet build` 编译检查
- 新增警告将被构建拒绝：修复警告（或经评审后以 `#pragma` / `.editorconfig` 降级并注明原因），不要绕过

## 本地运行

仓库中的 `appsettings.json` 不含任何密钥（`Database:ConnectionString` 与 `Jwt:Secret` 留空，缺失时启动即报错）。
敏感配置经 **User Secrets**（Development 环境）或环境变量注入，本地首次运行前执行：

```bash
# 1. 写入本地机密（存放于 %APPDATA%\Microsoft\UserSecrets\canexus-super-admin-api，不入 Git）
dotnet user-secrets set "Database:ConnectionString" "server=localhost;port=3306;database=super_admin;user id=root;password=你的密码;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none" --project src/API
dotnet user-secrets set "Jwt:Secret" "至少 32 字符的随机密钥" --project src/API

# 2. 集成测试连接串（SUPER_ADMIN_TEST_CONN 用户环境变量，缺失时测试启动即报错）
#    PowerShell（用户级，一次即可）：
[Environment]::SetEnvironmentVariable("SUPER_ADMIN_TEST_CONN", "server=localhost;port=3306;database=super_admin_test;user id=root;password=你的密码;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none", "User")

# 3. 启动（首次启动自动建库建表 + 写入种子数据）
dotnet run --project src/API

# 服务地址：http://localhost:5000（launchSettings 默认）
# API 文档：http://localhost:5000/openapi/v1.json（Development 环境）
# 种子超级管理员：admin / admin123
```

常用命令：

```bash
dotnet build                              # 编译
dotnet test                               # 全量测试（单元 + 集成）
dotnet test test/API.Tests                # 仅 API 集成测试
```

> 说明：集成测试强制从环境变量 `SUPER_ADMIN_TEST_CONN` 读取测试库连接串（代码中不内置任何密码回退值），测试库由 CodeFirst 自动创建；当前测试连接串覆盖对宿主 Program 配置存在失效问题，测试数据会写入开发库 `super_admin`，属已知遗留项。

## 部署说明

1. **发布**：`dotnet publish src/API -c Release -o publish`，产物为自包含目录（按目标运行时加 `--runtime` 参数）
2. **配置**：`appsettings.json` 不含密钥，生产环境必须经环境变量注入（双下划线 `__` 表示层级，优先级高于配置文件）：
   - `Database__ConnectionString` — 生产数据库连接串（`Database__DbType / Database__AutoCreateTable / Database__MigrationsEnabled` 等亦可覆盖；`MigrationsEnabled=false` 可关闭迁移机制）
   - `Jwt__Secret` — 生产签名密钥（随机生成，不与任何环境复用；更换后所有已签发令牌失效）
   - MultiTenancy__Enabled — 多租户开关
   - Outbox__Enabled / Outbox__PollIntervalSeconds / Outbox__MaxAttempts — 领域事件 Outbox 投递 worker 启停与重试参数
   - FileStorage__MaxSizeMB — 上传上限（Kestrel 请求体上限自动对齐为 MaxSizeMB + 1MB）
   - RateLimit__UseRedis / RateLimit__RedisConfiguration / RateLimit__RedisKeyPrefix — 分布式限流开关与 Redis 连接（多副本部署必须开启）
   - Cache__RedisEnabled / Cache__RedisConfiguration — 双层缓存 L2 分布式缓存开关与连接
   - 密钥缺失时应用启动即失败（Options 启动校验），不会带病运行
3. **数据库**：首次启动自动建库建表并写入种子数据；已有库表结构由 CodeFirst 增量补齐（不修改已有列类型，跨大版本结构变更需重建或手工迁移）。迁移机制（P1-A3）：启动时将 CodeFirst 模型表清单登记为 `sys_schema_migration.baseline-v1`（幂等），增量迁移按 `MigrationId` 顺序在事务内执行（脚本与记录同事务，失败整体回滚）；模型新增表但未登记迁移时启动校验直接报错并指明表名
4. **运行**：`dotnet API.dll`，或配置为 systemd / Windows 服务；前端生产环境由 nginx 将 `/api` 反向代理到本服务
5. **多副本部署**：`RateLimit__UseRedis=true` 让限流计数存 Redis，跨实例共享配额（关闭时回退为单副本进程内语义）；`Cache__RedisEnabled=true` 启用 L2 分布式缓存；`/health` 已豁免全局限流，可直接用于负载均衡探针。若前端经 nginx 反代，需启用 `Forwarded:Enabled` 并配置 `Forwarded:KnownProxies`，否则所有客户端将共享代理 IP 的限流配额
