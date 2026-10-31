# api-netcore — 后端服务

基于 .NET 10 的中后台管理系统后端，采用经典分层架构（API / Application / Domain / Infrastructure），内置 RBAC 权限、多租户、乐观并发、定时任务调度与完整审计能力。数据库结构由 SqlSugar CodeFirst 自动创建并填充种子数据，开箱即用。

## 技术路线

| 分类 | 技术 | 版本 |
| --- | --- | --- |
| 运行时 | .NET | 10.0 |
| ORM | SqlSugarCore（CodeFirst / 仓储 / 全局过滤器） | 5.1.4.221 |
| 数据库 | MySQL（驱动 MySqlConnector） | MySqlConnector 2.6.2 |
| 认证 | JWT（JwtBearer + System.IdentityModel.Tokens.Jwt） | 10.0.11 / 8.5.0 |
| 密码 | BCrypt.Net-Next 哈希 | 4.0.3 |
| 对象映射 | AutoMapper | 16.1.1 |
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
api-netcore/
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
- **仓储三分**：`IDomainRepository`（查询统计 + 投影查询（Select&lt;TDto&gt; 按属性名自动映射返回指定类型）+ 插入 + 物理删除，实体表 / 关联表公共；可空条件参数配合 WhereIF 按需拼接）/ `IAggregateRepository`（聚合专属：乐观并发更新、按主键删除、软删除、恢复；重写删除为软删除）/ `IAggregateTreeRepository`（树形查询）。接口只定义契约（无 new 约束），`new()` 仅出现在 SqlSugar 实现类上
- **关联表实体**：SysUserRole / SysRoleMenu / SysRoleDept 继承 `DomainBase`（非聚合根）：无独立主键，以业务字段联合主键唯一定位；具备创建审计与租户隔离（全局租户过滤器与聚合根一致），无乐观并发与更新审计语义，删除为物理删除
- **CQRS**：请求定义（record 实现 IRequest / ICommand / IQuery 接口层次）位于 Application.Contracts；处理器继承 RequestHandler → CommandHandler / QueryHandler 基类（Application/Abstractions，封装 MediatR 适配，子类实现 HandleAsync）；验证器独立于 Validators 目录，横切关注点（验证 / 日志 / 性能）由 Pipeline Behaviors 统一处理
- **DTO 继承体系**：`Application.Contracts/Common` 提供与领域对齐的基类——请求侧 `StatusRequest` / `StatusDescriptionRequest`（对齐 Status / Description），视图侧 `EntityVO` / `StatusVO`（对齐 Id / CreatedAt / Status）；各模块以 `XxxWriteRequest` / `XxxBaseVO` 抽取 Create-Update 与 List-Detail 共同字段，派生类零重复定义
- **校验分层**：请求级校验统一收敛于 **Validators 模块**（FluentValidation，`Application/Validators/`）：公共规则以扩展方法沉淀于 `Validators/Common/ValidationRules`（NotBlank / TrimmedMaxLength / Status / InRange / ConfigKey）；模块验证器按「请求级验证器（泛型，约束到 Write 基类 DTO）+ 命令验证器（SetValidator 委托）」组织，模块错误码经 Custom 规则透传；实体 setter 保留单属性不变量（Guard）作为领域最后防线
- **DTO → 实体映射**：AutoMapper **按实体拆分独立 Profile**（`Mappings/SysUserProfile` / `SysRoleProfile` / `SysMenuProfile` / `SysDeptProfile` / `SysDictProfile` / `SysConfigProfile` / `SysJobProfile` / `SysNoticeProfile` / `SysTenantProfile` / `SysLogProfile`，公共约定沉淀于 `MappingProfileBase`）；14 组 Create/Update 请求 → 实体映射 + 全局字符串值转换器自动 trim；实体 setter 同步归一化，双保险
- **富模型（setter 模式）**：实体属性 setter 内强制单属性不变量——trim 归一化（Domain.Shared/Validation/Guard）、非空、取值范围（状态 0/1、标签样式 1~5、数据范围 1~5、菜单类型等，经模块 Rules Ensure 方法）；跨属性耦合规则（如菜单类型 ↔ path/component/perms）由 `Create(...)` / `Apply(...)` 调用 Rules 校验，非法状态无法构造或逃逸
- **仓储分层**：`IDomainRepository`（查询 + 插入 + 物理删除 + **批量 DeleteRangeAsync / InsertRangeAsync**）→ `IAggregateRepository`（软删除 + 乐观并发）→ `IAggregateTreeRepository`（树查询）→ `IAncestorsTreeRepository`（**后代 Ancestors 批量刷新**，仅限实现 IAncestorsTree 的树形聚合如部门）；专属仓储：三张日志表 + `ISysRefreshTokenRepository`（GetActiveByTokenAsync / RevokeAllForUserAsync / RemoveExpiredAsync）
- **工作单元**：`IUnitOfWork` 仅暴露 `RunInTransactionAsync`（委托成功自动提交、异常自动回滚并原样透出），实现基于 SqlSugar `Ado.UseTranAsync`，无需显式 Begin / Commit / Rollback
- **端口归位**：领域技术端口（ITokenService / IPasswordHasher / IUnitOfWork / UserContext / ICaptchaService 等）定义于 Domain/Services，实现在 Infrastructure(.Shared)；缓存端口 `IAppCache` 与 TTL 档位定义于 Infrastructure.Shared/Caching（支持层共享设施），应用层经 DI 消费，不感知缓存技术细节
- **领域事件**：根基类 `DomainEvent` 仅承载共有元数据（事件名称 EventName = 短类名，即事件类型的唯一标识；发生时间 OccurredAt），**事件类型由事件类本身表达**，无独立类型字符串与弱类型载荷。泛型基类 `DomainEvent<TDomain>`（TDomain : DomainBase）携带事件关联的领域实体（Domain 属性）。**聚合级事件基类族按变更类型分层**：`AggregateCreatedEvent<TAggregate>`（Domain = 创建后状态）/ `AggregateUpdatedEvent<TAggregate>`（Domain = 变更后状态，Before = 变更前快照，发布点经 DomainBase.Snapshot 浅拷贝捕获）/ `AggregateDeletedEvent<TAggregate>`（Domain = 删除前状态）/ `AggregateRestoredEvent<TAggregate>`（恢复，预留）；无实体状态的事件（UserPasswordResetEvent，密码哈希敏感）与聚合根可空的日志事件（LogDeletedEvent）直接继承 DomainEvent。**事件文件按所属领域划分**（UserEvents / RoleEvents / MenuEvents / DeptEvents / DictEvents / ConfigEvents / NoticeEvents / TenantEvents / JobEvents / LogEvents），每领域按创建 / 修改 / 删除拆分具体事件类（如 UserCreatedEvent / UserUpdatedEvent / UserDeletedEvent）。发布端口 `IDomainEventPublisher` 定义于 Domain/Services/Events；全部业务写命令处理器在变更成功后的关键节点发布对应事件。应用层经 MediatR 非泛型通知信封（DomainEventNotification）进程内发布；监听器继承泛型基类 `DomainEventHandler<TEvent>`（屏蔽类型过滤守卫，TEvent 传 DomainEvent 即全量订阅）或直接实现 INotificationHandler 模式匹配（多类型监听器受类单继承限制）——**业务逻辑与响应逻辑解耦**，新增监听需求只需追加监听器实现，由 MediatR 扫描自动注册。内置监听器：权限缓存失效（用户事件→单用户，角色 / 菜单事件→全量）、字典缓存失效（类型迁移经 Before.DictType 比对新旧双失效）、配置缓存失效（键名变更经 Before.ConfigKey 比对新旧双失效）、事件审计（DomainEventAuditHandler，通用兜底记录事件名称与发生时间，不落实体状态以规避敏感数据）。**监听器命名约定**：`<响应关切><名词化响应动作>Handler`（如 PermissionCacheInvalidationHandler / DomainEventAuditHandler），统一继承泛型基类（单类型）或直接实现 INotificationHandler（多类型）。**可靠性边界（按需演进）**：事件为进程内同步分发（发布于业务事务提交后），无 Outbox / 重试 / 异步队列——当前监听器均为可自愈的内存缓存操作（缓存有 TTL 兜底），异常未做监听器间隔离（一个监听器抛异常会中断后续监听器并透传调用方）；接入消息推送等外部通知类监听器前，需补充 Outbox（至少一次投递）与监听器异常隔离策略。评估后不发布事件的写操作：登录 / 登出 / 刷新令牌（认证基础设施流程）、立即执行一次任务（不改变聚合数据）、操作 / 登录日志写入（OperLog 过滤器承载审计职责）
- **领域规则归位**：纯逻辑校验规则（MenuRules / DictRules / ConfigRules / JobRules / NoticeRules / LogQueryBounds）位于 Domain/Services/Rules，供处理器与 Domain.Tests 复用；树形装配统一由 `AggregateTreeBase<T>.BuildTree` 承接（同级按 OrderNum 升序再按 Id、孤儿节点挂根级），树查询处理器过滤后建树并经 AutoMapper 递归映射为契约 VO
- **双层缓存**：基于 HybridCache 的 L1 进程内存（短过期）+ L2 Redis（长过期，`HybridCache:RedisEnabled` 开启后生效）缓存，按 TTL 档位（Hot / Medium / Cold，可配置）自动调整两级过期策略；应用层统一经 `IAppCache` 端口读写与失效，不感知缓存技术

## 环境要求

- .NET SDK 10.0
- MySQL 8.x（本地默认 `localhost:3306`，账号 `root/***REMOVED***`，或通过配置修改）

## 构建约束与代码风格

- 各项目 `.csproj`（src 与 test 共 11 个项目）统一声明：**`TreatWarningsAsErrors`（编译警告即错误）** 与 **`EnforceCodeStyleInBuild`（构建期强制 `.editorconfig` 代码风格）**——不使用 `Directory.Build.props`，配置全部落位在项目文件内
- 代码风格基线由 `.editorconfig` 定义：风格类规则统一 **warning 级**，配合各项目的 `TreatWarningsAsErrors`，编译警告与代码风格违规在构建期均按**错误**处理（不合规代码无法通过构建）；格式统一经 `dotnet format api.slnx` 执行，各配置项的作用以 `.editorconfig` 内注释为准（该文件是规则与说明的唯一载体）
- **变量声明优先 `var`**：内置类型 / 类型明显 / 其余场景均用 `var`（`csharp_style_var_*` 三项均为 `true`）
- **类优先主构造函数 + `get` 只读属性**：依赖与构造参数经 C# 12 主构造函数承载，转存为 `{ get; }` 只读自动属性，避免传统构造函数字段回填样板（`csharp_style_prefer_primary_constructors`）
- 提交 `.cs` 文件时，仓库级 pre-commit 钩子（husky + lint-staged，见 `admin-vue/README.md`）会自动对其运行 `dotnet format --include`；推送前执行 `dotnet build` 编译检查
- 新增警告将被构建拒绝：修复警告（或经评审后以 `#pragma` / `.editorconfig` 降级并注明原因），不要绕过

## 本地运行

```bash
# 1. 修改数据库连接（可选，默认 localhost:3306 root/***REMOVED***）
#    编辑 src/API/appsettings.json → Database:ConnectionString

# 2. 启动（首次启动自动建库建表 + 写入种子数据）
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

> 说明：集成测试默认连接 `super_admin_test` 测试库（连接串可经环境变量 `SUPER_ADMIN_TEST_CONN` 覆盖），测试库由 CodeFirst 自动创建；当前测试连接串覆盖对宿主 Program 配置存在失效问题，测试数据会写入开发库 `super_admin`，属已知遗留项。

## 部署说明

1. **发布**：`dotnet publish src/API -c Release -o publish`，产物为自包含目录（按目标运行时加 `--runtime` 参数）
2. **配置**：生产环境通过 `appsettings.Production.json` 或环境变量覆盖：
   - `Database:ConnectionString` — 生产数据库连接串（`Database:DbType / AutoCreateTable / AutoSyncStructure` 等亦可覆盖）
   - `Jwt:Secret / Issuer / Audience` — 生产密钥（务必更换默认值）
   - `MultiTenancy:Enabled` — 多租户开关
3. **数据库**：首次启动自动建库建表并写入种子数据；已有库表结构由 CodeFirst 增量补齐（不修改已有列类型，跨大版本结构变更需重建或手工迁移）
4. **运行**：`dotnet API.dll`，或配置为 systemd / Windows 服务 / 容器；前端生产环境由 nginx 将 `/api` 反向代理到本服务
