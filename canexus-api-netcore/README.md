# canexus-api-netcore — 后端服务

基于 .NET 10 的中后台管理系统后端，采用经典分层架构（API / Application / Domain / Infrastructure），
内置 RBAC 权限、多租户、乐观并发、定时任务调度与完整审计能力。

数据库初始化按「库 / 结构 / 种子」三层分别判定：库不存在可按配置经管理连接创建，结构缺表时由
CodeFirst 依实体模型一次性建表、随后按序执行幂等迁移脚本（不保留任何迁移历史，开发期数据库可直接删除重建），
**生产环境不自动改表**；三层判定结论写入启动日志与 `/health/ready`。

## 目录

- [核心能力](#核心能力)
- [技术栈](#技术栈)
- [功能模块与接口](#功能模块与接口)
- [目录结构](#目录结构)
- [快速开始](#快速开始)
- [配置说明](#配置说明)
- [接口约定](#接口约定)
- [数据库初始化](#数据库初始化)
- [测试](#测试)
- [部署](#部署)
- [开发规范](#开发规范)
- [常见问题](#常见问题)
- [变更记录](#变更记录)
- [相关文档](#相关文档)

## 核心能力

- **认证授权**：JWT（访问令牌 + 刷新令牌轮换）、图形验证码、登录失败锁定、令牌吊销与在线会话强退。
- **RBAC**：用户 / 角色 / 菜单 / 部门 / 岗位五要素，权限点集中注册（`模块:动作:资源`），控制器声明与注册表在启动期双向校验。
- **数据权限**：按角色配置数据范围（全部 / 本部门 / 本部门及以下 / 仅本人等），查询层统一过滤。
- **多租户**：开关式共享库行级隔离；租户解析、归属校验、停用与到期拦截；超管可跨租户。
- **审计**：操作日志、登录日志、字段级实体变更日志（敏感值脱敏），异常路径同样留痕。
- **领域事件**：事务 Outbox 持久化 + 后台 worker 重试，权限 / 字典 / 配置缓存失效由监听器驱动。
- **可靠性**：乐观并发（版本令牌）覆盖更新 / 软删除 / 恢复三条写入路径（统一走 `ExecuteCommandWithOptLock`）、
  可空时间戳 `DeletedAt` 软删除与物理删除双通道显式分离、无历史依赖的幂等迁移框架、分布式锁、统一异常与响应结构。
- **可运维**：结构化日志（Serilog）、健康检查（存活 / 就绪）、限流、OpenTelemetry Trace 与 Metrics（可选）。

## 技术栈

| 分类 | 技术 | 版本 |
| --- | --- | --- |
| 运行时 | .NET | 10.0 |
| ORM | SqlSugarCore（CodeFirst / 仓储 / 全局过滤器） | 5.1.4.221 |
| 数据库 | MySQL 8.x（驱动 MySqlConnector） | 2.6.2 |
| 认证 | JwtBearer / System.IdentityModel.Tokens.Jwt | 10.0.14 / 8.23.0 |
| 密码哈希 | BCrypt.Net-Next | 4.2.1 |
| 对象映射 | AutoMapper（锁定 14.x：15.0.0 起改为商业 / RPL 双许可） | 14.0.0 |
| 请求分发 | MediatR（锁定 12.x：13.0.0 起改为商业许可；命令 / 查询 + Pipeline Behaviors，单库读写） | 12.5.0 |
| 输入验证 | FluentValidation（含 DI 扩展） | 12.2.0 |
| 任务调度 | Quartz（+ DI / Hosting / JSON 序列化，保留仍在维护的 3.x 线） | 3.22.9 |
| 缓存 | HybridCache（L1 内存 + L2 Redis）+ StackExchange.Redis | 10.11.0 / 3.4.31 |
| 日志 | Serilog.AspNetCore（控制台 + 滚动文件） | 10.0.0 |
| API 文档 | Microsoft.AspNetCore.OpenApi + Swashbuckle UI | 10.0.14 / 10.3.1 |
| API 版本化 | Asp.Versioning.Mvc（保留 8.x：10.x 引入新诊断与配置约束） | 8.1.1 |
| Excel 导入导出 | MiniExcel | 1.46.0 |
| 富文本清洗 | HtmlSanitizer | 11.0.16 |
| 可观测性 | OpenTelemetry（OTLP 导出） | 1.20.x |
| 测试 | NUnit + coverlet.collector（保留 4.x / 6.x：5.x 需重写大量断言） | 4.3.2 / 6.0.4 |

### 依赖合规与版本策略

全部直接依赖均为 MIT / Apache-2.0 等宽松许可，允许商业化使用，无 GPL / AGPL / SSPL 或需授权密钥的依赖。
版本升级以「最新稳定版 + 宽松许可」为准；若上游新版改为商业许可或带来破坏性迁移，则锁定最后一个宽松许可、
可稳定运行的版本，并在下表说明。审计时逐包核对 NuGet 注册信息的 license 表达式与 npm registry 的 license
字段，并确认 dist-tag 指向稳定版而非 rc / beta / preview。

| 依赖 | 当前（保留） | 最新稳定版 | 保留原因 |
| --- | --- | --- | --- |
| AutoMapper | 14.0.0（MIT） | 16.3.0 | 15.0.0 起改为 Lucky Penny 商业 / RPL 双许可，需许可证密钥；14.x 官方不回补 GHSA-rvv3-g6hj-g44x（High，DoS），若须修复应迁移到 MIT 替代库或购买商业许可 |
| MediatR | 12.5.0（Apache-2.0） | 14.3.0 | 13.0.0 起改为商业许可；12.5.0 是最后一个 Apache-2.0 版 |
| Quartz | 3.22.9（Apache-2.0） | 4.7.2 | 4.0 仅支持 net10.0，容器构建调度器、公开 API 大面积移除，且强制 QRTZ 表结构迁移；3.x 仍在维护 |
| Asp.Versioning.Mvc | 8.1.1（MIT） | 10.2.1 | 10.x 引入 AV0016 等新诊断与配置约束，需调整版本化配置 |
| NUnit / Test.Sdk / coverlet / adapter | 4.3.2 / 17.14.0 / 6.0.4 / 5.0.0 | 5.0.0 / 18.11.0 / 10.1.0 / 6.3.1 | 5.x 改变异步断言返回值与委托签名，需重写大量测试用例；当前组合稳定 |
| Vue | 3.5.45（MIT） | 3.5.45 | 原 package.json 与 workspace overrides 锁定 rc 预览版，已移除覆盖并固定稳定版 |
| TypeScript | 6.0.3（Apache-2.0） | 7.0.2 | Volar / vue-tsc 要求 typescript < 7，7.x 移除内部 tsc 导出导致类型检查无法启动；6.0.3 为兼容线最后稳定版 |

已知传递依赖风险：System.Security.Cryptography.Pkcs 8.0.1（经 JWT 相关包间接引入）存在 High 级公告
GHSA-846f-f764-76mx，需等待上游更新或显式提升传递依赖版本。

## 功能模块与接口

所有业务接口以 `/api` 为前缀，控制器统一标注 `[Authorize]` 与权限点；未显式 `[AllowAnonymous]` 的端点默认要求认证。

| 模块 | 路由前缀 | 说明 |
| --- | --- | --- |
| 认证授权 | `/api/auth` | 验证码、登录、刷新令牌、登出、修改密码、当前用户信息、前端路由树 |
| 用户管理 | `/api/system/user` | 分页 / 详情、新增、修改、删除、恢复、启停、重置密码、分配角色、导入、导出 |
| 角色管理 | `/api/system/role` | 分页 / 全量、增删改、恢复、启停、菜单授权、数据范围、导出 |
| 岗位管理 | `/api/system/post` | 分页、下拉、增删改、恢复 |
| 部门管理 | `/api/system/dept` | 树查询、增删改、恢复、祖级路径维护与环检测 |
| 菜单管理 | `/api/system/menu` | 目录 / 菜单 / 按钮树、增删改、恢复、动态路由 |
| 字典类型 | `/api/system/dict-type` | 分页、增删改、恢复 |
| 字典数据 | `/api/system/dict-data` | 分页、按类型查询、增删改、恢复 |
| 参数配置 | `/api/system/config` | 分页、按键取值、增删改、恢复（内置键受保护） |
| 通知公告 | `/api/system/notice` | 分页、详情、最新公告、增删改、恢复 |
| 文件管理 | `/api/system/file` | 上传、分页、详情、下载、删除 |
| 实体变更日志 | `/api/system/entity-change` | 字段级变更记录分页查询 |
| 租户管理 | `/api/system/tenant` | 分页 / 全量、增删改、恢复、租户占用校验 |
| 定时任务 | `/api/monitor/job` | 分页、增删改、恢复、启停、立即执行 |
| 任务日志 | `/api/monitor/job-log` | 执行日志分页 |
| 在线用户 | `/api/monitor/online` | 分页、按令牌强制下线 |
| 服务器监控 | `/api/monitor/server` | CPU / 内存 / 磁盘 / 运行时信息 |
| 缓存监控 | `/api/monitor/cache` | 缓存键统计与命中情况 |
| 操作日志 | `/api/system/oper-log` | 分页、清空、按条删除、Excel 导出 |
| 登录日志 | `/api/system/login-log` | 分页、清空、按条删除、Excel 导出 |

## 目录结构

```
canexus-api-netcore/
├── api.slnx                        # 解决方案（7 个生产项目 + 4 个测试项目）
├── scripts/
│   └── check-readme-types.py       # README 类型引用校验（CI 门禁）
├── src/
│   ├── API/                        # 宿主层
│   │   ├── Controllers/            # REST 控制器（20 个，统一 {id:guid}/{version:guid} 路由约束）
│   │   ├── Authorization/          # 权限特性与强制改密白名单特性
│   │   ├── Filters/                 # 权限过滤器、操作日志过滤器
│   │   ├── Middlewares/            # 异常、租户、TraceId、请求体上限、强制改密
│   │   ├── HealthChecks/           # 存活 / 就绪检查与 JSON 报告
│   │   ├── RateLimiting/           # 全局限流与认证端点限流策略
│   │   ├── BackgroundServices/     # Outbox 投递 worker
│   │   ├── Extensions/             # 认证、安全、缓存、文档、版本化、可观测性等注册扩展
│   │   └── appsettings*.json       # 配置模板（不含任何密钥）
│   ├── Application/                # 应用层：命令 / 查询处理器、验证器、映射、横切行为
│   │   ├── Abstractions/           # 处理器基类（封装 MediatR 适配）
│   │   ├── Commands/               # 写操作处理器（按实体合并单文件）
│   │   ├── Queries/                # 读操作处理器
│   │   ├── Validators/             # FluentValidation 验证器与公共规则
│   │   ├── Behaviors/              # 验证 / 事务 / 数据范围 / 审计 / 幂等 / 性能
│   │   ├── Events/                 # 领域事件监听器（缓存失效、事件审计）
│   │   ├── Mappings/               # AutoMapper Profile（按实体拆分）
│   │   ├── Permissions/            # 权限校验与缓存
│   │   ├── Scopes/                 # 数据范围过滤
│   │   └── Security/               # 富文本清洗等安全组件
│   ├── Application.Contracts/      # 契约层：请求 record、DTO、统一响应
│   ├── Domain/                     # 领域层：实体、领域事件、仓储接口、领域端口
│   │   ├── Entities/               # 基类体系 + Sys* 业务实体 + Outbox 实体
│   │   ├── Events/                 # 按领域划分的事件类
│   │   ├── Repositories/           # 领域 / 聚合 / 树形仓储接口
│   │   ├── Services/               # 令牌、密码、验证码、调度、工作单元等端口
│   │   └── ValueObjects/           # 值对象
│   ├── Domain.Shared/              # 共享内核：错误码、分页、权限定义、枚举、异常、本地化
│   ├── Infrastructure/             # 基础设施实现
│   │   ├── DataAccess/             # 三层初始化、迁移执行通道、SqlSugar 工厂、种子数据
│   │   ├── Repositories/           # 仓储实现与工作单元
│   │   ├── Auth/ Users/ Tenants/   # 令牌、当前用户、租户上下文
│   │   ├── Caching/ Locking/ Redis/# 缓存监控、分布式锁、Redis 连接
│   │   ├── Scheduling/             # Quartz 调度与作业执行包装
│   │   ├── Notifications/ Storage/ # 邮件通知、本地文件存储
│   │   └── Security/               # 内容签名、脱敏等
│   └── Infrastructure.Shared/      # 基础设施共享：配置选项、双层缓存、数据过滤、日志上下文
└── test/
    ├── Domain.Tests/               # 领域规则、实体不变量、分页与错误码
    ├── Infrastructure.Tests/       # 真实 MySQL 集成测试 + 架构约束测试
    ├── Application.Tests/          # 验证器、映射、权限、事件、处理器单测
    └── API.Tests/                  # WebApplicationFactory 全链路 HTTP 测试
```

## 快速开始

### 1. 环境要求

| 依赖 | 版本 | 是否必需 |
| --- | --- | --- |
| .NET SDK | 10.0.x | 必需 |
| MySQL | 8.0+ | 必需 |
| Redis | 7.x | 可选；生产环境必填（`Cache:RedisEnabled` 强制为 true） |
| Python | 3.8+ | 可选，仅执行 README 类型校验脚本时需要 |
| Node.js + pnpm | 仓库要求见根 `package.json` | 可选，仅启用 Git 钩子时需要 |

### 2. 还原依赖

```bash
git clone <仓库地址>
cd canexus-api-netcore
dotnet restore api.slnx
```

### 3. 写入本地机密

仓库中的 `appsettings.json` **不含任何密钥**（`Database:ConnectionString` 与 `Jwt:Secret` 均为空，
非 Development 环境缺失时启动即失败）。本地开发推荐使用 User Secrets（存放于
`%APPDATA%\Microsoft\UserSecrets\canexus-super-admin-api`，不入 Git）：

```bash
dotnet user-secrets set "Database:ConnectionString" \
  "server=localhost;port=3306;database=super_admin;user id=root;password=你的密码;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none" \
  --project src/API

dotnet user-secrets set "Jwt:Secret" "按 UTF-8 编码不少于 32 字节的随机密钥" --project src/API
```

> 数据库需提前存在，或保留 `Database:AutoCreateDatabase=true` 让应用用管理连接建库（Development 默认允许）。
> 也可以改用环境变量注入，见[配置说明](#配置说明)。

### 4. 启动服务

```bash
dotnet run --project src/API
```

| 地址 | 说明 |
| --- | --- |
| `http://localhost:5000` | 默认监听地址（见 `Properties/launchSettings.json`） |
| `http://localhost:5000/swagger` | Swagger UI（仅 Development） |
| `http://localhost:5000/openapi/v1.json` | OpenAPI 3.1 文档（仅 Development） |
| `http://localhost:5000/health/live` | 存活探针（匿名） |
| `http://localhost:5000/health/ready` | 就绪探针（含数据库 / 缓存 / 调度器，匿名） |

首次启动会按「库 / 结构 / 种子」三层完成初始化并写入内置数据。内置超管账号为 `admin`，初始口令来自：

- 配置项 `Bootstrap:AdminInitialPassword`（非 Development 环境必填）；
- Development 未配置时随机生成，**仅在控制台输出一次**。

首次登录返回 `mustChangePassword=true`，改密前访问其他业务接口会被拦截（403 / 40302）。

### 5. 调通第一个接口

登录需要图形验证码，建议直接在 Swagger UI 中操作：

1. `GET /api/auth/captcha` → 响应 `data.captchaId` 与 `data.imageBase64`（SVG data URI，有效期 5 分钟、一次性）；
2. `POST /api/auth/login`，请求体：

```json
{
  "userName": "admin",
  "password": "你的初始口令",
  "captchaId": "上一步返回的 captchaId",
  "captchaCode": "图片中的字符"
}
```

3. 复制响应中的 `data.accessToken`，在 Swagger 右上角 **Authorize** 中粘贴（无需手写 Bearer 前缀）；
4. 之后即可调用受保护接口，例如 `GET /api/auth/user-info`。

等价的命令行方式：

```bash
curl -s http://localhost:5000/api/auth/captcha

curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"userName":"admin","password":"你的初始口令","captchaId":"...","captchaCode":"...."}'

curl -s http://localhost:5000/api/auth/user-info \
  -H "Authorization: Bearer <accessToken>"
```

## 配置说明

### 加载顺序

配置按以下顺序覆盖（后者优先）：`appsettings.json` → `appsettings.{Environment}.json` → User Secrets
（仅 Development）→ 环境变量 → 命令行参数。环境变量用双下划线表示层级，例如
`Database__ConnectionString`、`Cache__RedisEnabled`。

所有配置节在启动时统一绑定并校验（继承 `OptionBase` 的选项类），任何一项非法都会在启动阶段报错，
不会带病运行。

### 主要配置项

**数据库与初始化**

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `Database:ConnectionString` | 空 | MySQL 连接串，必填 |
| `Database:AutoCreateDatabase` | `true` | 库不存在时是否用管理连接创建 |
| `Database:AutoCreateTable` | `true` | 首次初始化是否建表；Production 必须为 `false` |
| `Database:AutoSyncStructure` | `true` | 是否同步表结构；Production 必须为 `false` |
| `Database:AllowSchemaInitialization` | `false` | 全新生产库首次建库建表的一次性授权 |
| `Database:MigrationsEnabled` | `true` | 是否启用迁移执行通道（按序执行全部幂等 `ISchemaMigration` 脚本） |
| `Database:ValidateModelStructure` | `false` | 启动时比对实体模型与库表列 / 主键，发现绕过 CodeFirst / 迁移通道的改表 |

**认证与安全**

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `Jwt:Secret` | 空 | 签名密钥，UTF-8 不少于 32 字节，必填 |
| `Jwt:Issuer` / `Jwt:Audience` | 默认值 SuperAdmin | 签发者与受众 |
| `Jwt:AccessTokenMinutes` | `30` | 访问令牌有效期（分钟） |
| `Jwt:RefreshTokenDays` | `7` | 刷新令牌有效期（天） |
| `Security:PasswordPolicy` | 8-20 位，含字母 / 数字 / 大写 | 自助改密与管理员重置共用 |
| `Security:LoginLock:MaxFailedAttempts` | `5` | 连续失败锁定阈值 |
| `Security:LoginLock:LockMinutes` | `10` | 锁定时长（每次失败滑动重置） |
| `Bootstrap:AdminInitialPassword` | 空 | 内置超管初始口令；非 Development 必填 |
| `Bootstrap:SeedDemoData` | `true` | 是否写入演示数据；Production 恒定不写入 |

**集成与运行时**

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `MultiTenancy:Enabled` | `false` | 多租户开关；Production 必须显式声明 |
| `Cache:RedisEnabled` | `false` | L2 分布式缓存开关；Production 必须为 `true` |
| `Cache:RedisConfiguration` | `localhost:6379` | Redis 连接串 |
| `Cache:Hot/Medium/Cold` | 见配置文件 | 三档 TTL（本地 / 分布式秒数） |
| `Outbox:Enabled` | `true` | 领域事件 Outbox worker 开关 |
| `Outbox:PollIntervalSeconds` | `5` | 轮询间隔 |
| `Outbox:MaxAttempts` | `5` | 最大投递次数，超限转失败状态 |
| `Scheduling:ThreadCount` | `10` | Quartz 工作线程数 |
| `RateLimit:Enabled` | `true` | 限流总开关 |
| `RateLimit:GlobalPermitLimit` | `0` | 全局每窗口请求数；`0` 表示不启用，Production 必须大于 0 |
| `RateLimit:AuthPermitLimit` | `100` | 验证码 / 登录 / 刷新令牌端点配额 |
| `RateLimit:UseRedis` | `false` | 分布式限流；多副本部署必须开启 |
| `FileStorage:MaxSizeMB` | `10` | 上传大小上限（Kestrel 请求体上限自动对齐为 +1MB） |
| `FileStorage:RootPath` | `uploads` | 本地存储根目录 |
| `Export:MaxRows` | `10000` | 单次导出最大行数，超出截断并在文件名标注 |
| `Otel:Enabled` | `false` | OpenTelemetry 采集开关 |
| `Otel:OtlpEndpoint` | 空 | OTLP 导出端点，如 `http://otel-collector:4317` |
| `Mail:Enabled` | `false` | 邮件通知渠道开关 |

**传输与部署**

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `Https:RedirectEnabled` | `false` | HTTP 强制跳转 HTTPS；Production 必须为 `true` |
| `Https:HstsEnabled` | `false` | 输出 HSTS 头；Production 必须为 `true` |
| `Https:HstsMaxAgeDays` | `180` | HSTS 有效期 |
| `Cors:Enabled` | `false` | 跨域开关 |
| `Cors:AllowedOrigins` | `http://localhost:5173` | 允许的来源（精确匹配） |
| `Forwarded:Enabled` | `false` | 反向代理转发头解析 |
| `Forwarded:KnownProxies` | 空 | 可信代理白名单；开启转发头时必填 |

### 生产环境硬性基线

`ProductionConfigurationValidator` 在启动时一次性校验以下项，任一不合规即列出全部问题并阻止启动：

| 配置 | 要求 |
| --- | --- |
| `Jwt:Secret` | 非空，不得为示例值或开发回退值（非 Development 校验） |
| `Bootstrap:AdminInitialPassword` | 非空（非 Development 校验） |
| `Bootstrap:RequirePasswordChangeOnSeed` | 不得显式设为 `false` |
| `Database:AutoCreateTable` | `false` |
| `Database:AutoSyncStructure` | `false` |
| `Https:RedirectEnabled` | `true` |
| `Https:HstsEnabled` | `true` |
| `RateLimit:Enabled` 且 `RateLimit:GlobalPermitLimit` | `true` 且大于 0 |
| `MultiTenancy:Enabled` | 显式 `true` / `false` |
| `Forwarded:Enabled=true` 时 `Forwarded:KnownProxies` | 非空 |
| `Cache:RedisEnabled` | `true`（会话吊销与缓存失效依赖跨实例统一视图） |

## 接口约定

### 统一响应

除文件下载等二进制流接口外，所有 JSON 接口（含异常、401 挑战、限流）返回同一结构：

```json
{ "code": 200, "message": "操作成功", "data": { } }
```

无数据载荷时省略 `data`。`code` 为业务码，HTTP 状态码由业务码映射（见下表）。

### 错误码分段

| 区间 | 含义 | HTTP |
| --- | --- | --- |
| `400xx` | 通用 / 公共（参数、不存在、重复、并发冲突、限流） | 400 / 429 |
| `401xx` | 认证（未登录、过期、撤销、账号锁定） | 401 |
| `403xx` | 授权（无权限、跨租户、须先改密） | 403 |
| `4002xx`、`4003xx`… | 业务模块段（用户、角色、菜单、部门……） | 400 |
| `50000` | 系统内部错误 | 500 |

完整定义见 `src/Domain.Shared/Results/ErrorCodes.cs`。新增错误码必须同时补充
`Localization/ErrorMessages_zh-CN.json` 与 `ErrorMessages_en-US.json`，否则启动期校验失败。

### 认证流程

1. `GET /api/auth/captcha` 获取验证码（一次性、5 分钟有效）；
2. `POST /api/auth/login` 获取令牌对：`accessToken` / `refreshToken` / `expiresIn` / `mustChangePassword`；
3. 后续请求携带 `Authorization: Bearer <accessToken>`；
4. 访问令牌过期用 `POST /api/auth/refresh` 轮换（旧刷新令牌立即失效）；
5. `POST /api/auth/logout` 吊销刷新令牌（不传时吊销当前用户全部活跃令牌）。

刷新令牌以 SHA-256 哈希落库，数据库不存明文；登出、改密、管理员重置密码或强退后，已签发令牌立即失效
（401 / 40107）。

### 分页

请求参数 `pageIndex`（从 1 开始，默认 1）与 `pageSize`（默认 10，上限 500，越界自动收敛）。
响应 `data` 结构：

```json
{ "items": [], "total": 0, "pageIndex": 1, "pageSize": 10 }
```

### 乐观并发

聚合根（继承 `AggregateBase`）以 `Version`（GUID）作为版本令牌，**更新、软删除、恢复三条写入路径
统一经 SqlSugar 的 `ExecuteCommandWithOptLock` 落地**，避免并发覆盖与「墓碑被旧快照复活」。
软删除改用可空时间戳 `DeletedAt`（UTC）：null 为未删除，非 null 为删除时间，恢复即重新置为 null；
全局查询过滤器按 `DeletedAt IS NULL` 过滤。

| 路径 | 入口 | 并发控制方式 |
| --- | --- | --- |
| 更新 | `UpdateAsync` | 实体全字段更新，`UPDATE ... WHERE Id = @id AND Version = 传入值`，命中后框架自动轮换版本令牌 |
| 软删除 | `SoftDeleteAsync`（按主键 / 主键集合 / 条件） | 按列只更新 `DeletedAt` / 更新审计 / `Version`，同样走 `ExecuteCommandWithOptLock`，自动轮换版本令牌 |
| 恢复 | `RestoreAsync`（按主键 / 主键集合 / 条件） | 同上，`DeletedAt` 置回 null；版本轮换使旧令牌持有的更新失效 |
| 物理删除 | `HardDeleteAsync` | 单条 DELETE，不参与乐观锁，不可恢复（仅限关联表 / 合规擦除 / 测试夹具） |

使用方式：

- 修改与删除请求的路由必须携带最新 `version`（GUID 版本令牌），例如 `PUT /api/system/user/{id}/{version}`；
- 版本不一致或目标不可见时统一抛业务码 `40005`（CommonConcurrencyConflict），客户端应刷新后重试；
- 携带版本令牌时以其作为 CAS 条件（陈旧令牌必然冲突）；未携带的批量 / 后台路径取库中当前版本，
  仍由框架校验并轮换，但保持幂等静默忽略语义（无匹配返回 false）；
- 批量软删除 / 恢复在无外层事务时自持事务，保证多行写入原子性；
- 仓储层调用示例见「开发规范 → 仓储删除语义」。

### 多租户

开启 `MultiTenancy:Enabled` 后，业务请求携带 `X-Tenant-Id`。服务端核对请求头与 JWT 租户声明，
并按租户状态拦截：

| 场景 | 业务码 |
| --- | --- |
| 跨租户访问 | `40301` |
| 租户不存在 / 平台请求携带租户头 | `400801` |
| 租户已停用 | `400805` |
| 租户已到期 | `400806` |

关闭多租户时上述校验全部放行。

### API 版本化

当前版本 `1.0`，默认生效，路径不带版本段。需要显式指定时使用查询参数 `api-version=1.0`
或请求头 `X-Api-Version: 1.0`；响应头 `api-supported-versions` 声明支持集。

### 本地化

默认 `zh-CN`，支持 `en-US`，按 `Accept-Language` 协商，业务异常消息随文化翻译。

### 权限模型

- 权限点集中声明于 `PermissionDefinitions`，命名约定 `模块:动作:资源`（如 `system:user:list`）；
- 控制器通过 `[RequirePermission]` 引用常量，编译期检查；启动期扫描控制器声明与注册表比对，未注册即拒绝启动；
- 超级管理员持有 `*:*:*`；普通角色按菜单 / 按钮授权，越权返回 403 / 40300；
- 数据范围（全部 / 自定义部门 / 本部门 / 本部门及以下 / 仅本人）在查询层统一拼接过滤条件。

## 数据库初始化

启动时按三层分别判定，结论写入日志与 `/health/ready` 的 `initialization` 字段（database / schema / seed）：

| 层 | 判定方式 | 行为 |
| --- | --- | --- |
| 库 | 用剥离库名的管理连接查 `information_schema.SCHEMATA` | 不存在且允许时建库，否则明确拒绝启动 |
| 结构 | 按 `information_schema.TABLES` 找出缺失的模型表 | 缺表时 CodeFirst 一次性建表并修正为 InnoDB；表齐全时**不执行任何建表 DDL**。随后按序执行全部幂等迁移脚本 |
| 种子 | 按稳定键幂等补齐 | 只补缺失项，不覆盖已修改数据 |

框架处于开发初期，**所有数据库都是临时的，可直接删除重建**：

```sql
DROP DATABASE IF EXISTS super_admin_test;   -- 开发 / 测试库同理
```

重建后下一次启动会依次完成建库（如允许）、CodeFirst 建表、InnoDB 引擎修正、
`QuartzSchemaInitializer` 幂等建立 `QRTZ_` 集群表、种子补齐。因此本框架**不保留任何迁移历史**：
没有 `sys_schema_migration` 表、没有基线登记、没有已应用记录 / 脚本指纹 / 版本状态机，
`/health/ready` 也只输出三层状态。

后续如需 CodeFirst 无法表达的结构变更（索引、约束、数据订正），扩展方式：

1. 新建 `ISchemaMigration` 实现，`MigrationId` 按 `vNNN` 递增，`Scripts` 必须是幂等 SQL
   （`CREATE TABLE IF NOT EXISTS`、`INSERT ... ON DUPLICATE KEY UPDATE` 等）；
2. 在 `DependencyExtensions` 中逐个注册 `AddSingleton<ISchemaMigration, XxxMigration>()`；
3. `SchemaMigrationCatalog` 负责排序并拒绝重复 / 非法 ID，`SchemaMigrator` 在事务内按序执行
   （原生 SQL 经 `ITransactionSqlRunner` 挂接事务；MySQL DDL 有隐式提交，故脚本必须可重复执行）。

- 生产环境禁止应用自动改表（`Database:AutoCreateTable` / `Database:AutoSyncStructure` 必须为 false）；
- 全新生产库需显式设置 `Database:AllowSchemaInitialization=true` 完成一次性初始化，随后移除该项；
- 可选开启 `Database:ValidateModelStructure`，比对实体模型与实际表结构，发现绕过 CodeFirst / 迁移通道的改表。

## 测试

```bash
# 全量测试（单元 + 真实 MySQL 集成）
dotnet test api.slnx

# 仅某一层
dotnet test test/API.Tests
dotnet test test/Domain.Tests

# README 类型引用校验（CI 首道门禁）
python3 scripts/check-readme-types.py   # Windows 本机若 python3 为商店占位程序，改用 python
```

集成测试**强制**从环境变量 `SUPER_ADMIN_TEST_CONN` 读取测试库连接串，代码中不含任何密码回退值：

```bash
# Bash
export SUPER_ADMIN_TEST_CONN="server=localhost;port=3306;database=super_admin_test;user id=root;password=你的密码;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none"

# PowerShell（用户级，一次即可）
[Environment]::SetEnvironmentVariable("SUPER_ADMIN_TEST_CONN", "server=localhost;port=3306;database=super_admin_test;user id=root;password=你的密码;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none", "User")
```

测试宿主会断言最终生效的连接串非空且库名不是开发库 `super_admin`，覆盖失效即快速失败，不会静默写开发库。

| 测试项目 | 覆盖内容 |
| --- | --- |
| `Domain.Tests` | 实体不变量、领域规则、分页、错误码与本地化资源 |
| `Infrastructure.Tests` | 真实 MySQL 下的过滤器、乐观锁、迁移、Outbox、调度，以及架构约束 |
| `Application.Tests` | 验证器、映射、权限校验、事件监听、处理器逻辑 |
| `API.Tests` | 认证、系统管理、监控等全链路 HTTP 行为与 Redis 降级 |

CI（`.github/workflows/backend-ci.yml`）依次执行：README 类型校验 → 还原 → 编译 → 格式检查 → 全量测试。

## 部署

### 1. 发布

```bash
dotnet publish src/API -c Release -o publish
# 指定运行时：dotnet publish src/API -c Release -r linux-x64 --self-contained -o publish
```

### 2. 环境变量

生产环境必须经环境变量或密钥管理系统注入，至少包含：

```bash
Database__ConnectionString="server=...;database=...;user id=...;password=...;CharSet=utf8mb4;"
Jwt__Secret="不少于 32 字节的随机密钥"
Bootstrap__AdminInitialPassword="首次部署的 admin 口令"
Database__AutoCreateTable=false
Database__AutoSyncStructure=false
Https__RedirectEnabled=true
Https__HstsEnabled=true
RateLimit__Enabled=true
RateLimit__GlobalPermitLimit=600
MultiTenancy__Enabled=false
Cache__RedisEnabled=true
Cache__RedisConfiguration="redis:6379"
```

### 3. Docker Compose（本地 / 内网验证）

`docker-compose.yml` 会拉起 MySQL、Redis 与 API。先准备 `.env`：

```dotenv
MYSQL_ROOT_PASSWORD=请替换为强口令
JWT_SECRET=请替换为不少于32字节的随机密钥
ADMIN_INITIAL_PASSWORD=请替换为符合口令策略的初始口令
```

```bash
docker compose up --build
# API: http://localhost:8080  （健康检查 /health/ready）
```

说明：容器默认以 Staging 环境运行（`ASPNETCORE_ENVIRONMENT` 可覆盖），只强制密钥类基线，便于内网直接
HTTP 访问。正式生产请在 Kubernetes / 反向代理后以 Production 环境运行，并补齐上一节的全部基线配置。

### 4. 反向代理与多副本

- 前端由 nginx 将 `/api` 反向代理到本服务；后端在代理之后时必须设置 `Forwarded__Enabled=true` 与
  `Forwarded__KnownProxies`，否则限流与操作日志只能看到代理 IP；
- 多副本部署需 `Cache__RedisEnabled=true`（会话吊销、缓存失效广播）与 `RateLimit__UseRedis=true`
  （跨实例共享限流配额）；
- `/health/live` 与 `/health/ready` 已豁免认证与全局限流，可直接作为负载均衡 / K8s 探针。

## 开发规范

### 构建门禁

- 全部 11 个项目均声明 `TreatWarningsAsErrors` 与 `EnforceCodeStyleInBuild`：编译警告与代码风格违规
  在构建期按错误处理；
- 代码风格基线见 `.editorconfig`（变量优先 `var`、类优先主构造函数 + `get` 只读属性等），
  统一用 `dotnet format api.slnx` 修复；
- 仓库级 Git 钩子：提交前对 `.cs` 执行 `dotnet format`，推送前执行前端类型检查、单元测试与后端编译；
- 不要通过降级规则或 `#pragma` 绕过警告；确需例外时在代码中注明原因并经评审。

### 仓储删除语义

物理删除与软删除是两条**显式分离**的通道，不再按实体基类隐式改写（避免同一方法名在不同静态类型上语义漂移）：

- `HardDeleteAsync`：由 `IDomainRepository<T>` 提供，单条 DELETE 物理移除，不写字段级变更日志、不轮换版本令牌，不可恢复；
- `SoftDeleteAsync`：由 `IAggregateRepository<T>` 提供（按主键 / 主键集合 / 条件重载），写入 `DeletedAt`
  时间戳墓碑（经 `ExecuteCommandWithOptLock` 按列更新），自动轮换版本令牌并兜底填充更新审计，
  可经 `RestoreAsync` 把 `DeletedAt` 置回 null 恢复；
- 关联表 / 日志表等无墓碑语义的实体用物理删除；聚合根默认用软删除，确需彻底清除（合规擦除、测试夹具）时显式调用物理删除；
- 仓储子类按业务需要选择调用，不得用同名方法改写语义：两种语义各有一个明确入口。

使用方式（仓储层直接调用）：

```csharp
// 聚合根：软删除（命令携带 version 时附加 Version CAS 条件）
await userRepository.SoftDeleteAsync(command.Id, ct, command.Version);

// 聚合根：恢复（同样可携带版本令牌）
await userRepository.RestoreAsync(command.Id, ct, command.Version);

// 关联表 / 日志表：物理删除（单条 DELETE，不可恢复）
await userRoleRepository.HardDeleteAsync(ur => ur.UserId == command.Id, ct);
```

- 聚合根删除命令处理器统一调用 `SoftDeleteAsync`；原有同名删除方法已从仓储接口拆分，编译期即可防止误用；
- 关联表替换（角色-菜单、用户-角色）统一调用 `HardDeleteAsync`；
- 需要彻底清除聚合根数据时，显式调用 `HardDeleteAsync` 并自行承担不可恢复与审计缺失风险。

### 新增功能检查清单

1. 权限点先在 `PermissionDefinitions` 声明，控制器引用常量（启动期会校验注册表一致性）；
2. 写命令必须同时提供验证器，否则 `WriteCommandValidatorStartupValidator` 会在启动时失败；
3. 新增错误码同步补充 zh-CN / en-US 两份本地化资源；
4. 需要前端路由的功能，同步补充菜单种子与按钮权限；
5. 开发期数据库可直接删除重建；CodeFirst 无法表达的结构变更追加幂等 `ISchemaMigration` 脚本，禁止依赖生产环境自动改表；
6. 补充对应层级的测试。

### 任务完成后的固定动作（强制交付门禁）

适用范围：本仓库后续**所有**任务与操作——功能开发、Bug 修复、重构、依赖包（NuGet / npm）增减或升级、
配置 / 脚本 / 文档 / 规范调整、测试补充等。只要产生待提交改动，一律按以下顺序执行：不得跳过、不得调换、
不得因「只是文档或配置改动」而省略，也无需另行确认。**推送以 gitee 为准**：GitHub 可达时同步推两端，
GitHub 推送失败时仅推送 gitee 即可。

1. **立即运行全量测试**：`dotnet test api.slnx`，确认全部用例通过
   （集成测试需先设置 `SUPER_ADMIN_TEST_CONN` 为有效连接串，且测试库必须是 `super_admin_test`）；
   出现失败必须先修复并复跑至全绿，禁止带红提交；
2. **同步 README**：补充功能说明、使用方式与本次变更记录；任何影响安装、配置、接口、目录结构、
   依赖版本、运维方式或开发规范的改动同样必须更新本文件；提交前执行 `python3 scripts/check-readme-types.py`
   与 `dotnet format api.slnx --verify-no-changes`（本机 `python3` 为商店占位程序，改用 `python`）；
3. **提交并推送**：测试全部无误后 `git add` 本次相关修改并 `git commit`，随后推送 `gitee`；
   GitHub 可达时再推 `origin`（GitHub）：

   ```bash
   git push gitee master
   git push origin master
   ```

   **GitHub 推送失败（代理 502 / 网络超时 / 隧道不通等）时，仅推送 gitee 即可**：不反复重试、
   不阻塞交付、不视为交付失败，在结果中说明 GitHub 未同步及原因即可；后续网络恢复可顺手补推，
   但不作为待办阻塞项。
4. **收尾校验**：确认 `git status` 干净（还原 pre-push hook 生成的前端声明文件等噪音），
   核对 local / gitee refs 一致；GitHub 本次推送成功时再一并核对三端 refs。

## 常见问题

### 启动报错：`Jwt:Secret` 必须显式提供

非 Development 环境不允许使用示例值或开发回退密钥。执行：

```bash
dotnet user-secrets set "Jwt:Secret" "不少于 32 字节的随机字符串" --project src/API
```

生产环境用环境变量 `Jwt__Secret` 注入。更换密钥会使全部已签发令牌立即失效。

### 启动报错：`Bootstrap:AdminInitialPassword` 必须显式提供

非 Development 环境必须提供内置超管初始口令；Development 未提供时会随机生成并在控制台输出一次。
口令需满足 `Security:PasswordPolicy`（默认 8-20 位，含字母与数字）。

### Production 启动失败，列出多项「基线校验失败」

这是刻意设计的 fail-fast：按提示逐项补齐[生产环境硬性基线](#生产环境硬性基线)即可，不会逐项试错。
最常见的三项是 `Https:RedirectEnabled`、`Https:HstsEnabled` 与 `RateLimit:GlobalPermitLimit`。

### 忘记 admin 密码 / 登录后提示必须先改密

- 首次登录返回 `mustChangePassword=true` 属正常流程，改密前访问业务接口会返回 403 / 40302；
  调用 `POST /api/auth/change-password` 后重新登录即可；
- 忘记口令可让其他超管在用户管理中重置，或直接更新数据库中该用户的密码哈希（不推荐，需同时清理
  刷新令牌与强制改密标记）。

### 接口返回 403，提示无权限

1. 确认账号角色已勾选对应菜单 / 按钮权限；
2. 确认前端路由与按钮权限串与后端 `PermissionDefinitions` 一致；
3. 超管持有 `*:*:*`，若超管也 403，检查令牌中的角色声明与 `MultiTenancy:Enabled` 配置；
4. 多租户场景下的跨租户访问固定返回 40301。

### 接口返回 401，`code` 为 40102 或 40107

`40102` 表示访问令牌过期，调用 `/api/auth/refresh` 换取新令牌；`40107` 表示令牌已被吊销
（登出、改密、管理员强退、角色 / 权限变更），需重新登录。多副本部署若未开启 `Cache:RedisEnabled`，
吊销状态无法跨实例同步，会出现「某些请求仍可用」的现象。

### 开发期数据库结构改乱了 / 想重建

框架不保留迁移历史，直接删除重建即可（**仅限开发 / 测试库**）：

```sql
DROP DATABASE IF EXISTS super_admin_test;
```

下一次启动会按「库 → 结构（CodeFirst + 幂等迁移 + QRTZ_ 表）→ 种子」自动重建。
生产库不可这样操作，仍须由运维按 `Database:AllowSchemaInitialization` 授权一次性初始化。

### 集成测试提示 `SUPER_ADMIN_TEST_CONN` 未设置

按[测试](#测试)一节设置环境变量后重开终端。注意测试库名不能是 `super_admin`，否则宿主会主动失败，
避免误写开发库。

### 上传文件返回 413

请求体超过 `FileStorage:MaxSizeMB`（默认 10MB）。该上限同时约束 Kestrel 与业务校验层，修改配置后需重启；
反向代理（nginx `client_max_body_size`）也要同步放宽。

### 前端跨域预检失败

开发环境可设置 `Cors:Enabled=true` 并把前端地址加入 `Cors:AllowedOrigins`；生产推荐同源部署或由
nginx 统一反代，避免开启宽泛的 CORS。

### 反向代理后所有用户共享同一限流配额

服务端只看到代理 IP。设置 `Forwarded:Enabled=true`，并把代理地址写入 `Forwarded:KnownProxies`，
即可还原真实客户端 IP。

### 访问 `/swagger` 返回 404

Swagger 与 OpenAPI 文档仅在 Development 环境挂载。生产环境请通过 CI 产物或本地生成的 OpenAPI 文档对接。

### Redis 是必需的吗

Development 可不启用（缓存退化为进程内 L1、限流失效为单实例语义）；Production 强制 `Cache:RedisEnabled=true`，
因为会话吊销、权限 / 令牌版本失效与分布式限流都依赖跨实例统一视图。

### `dotnet format` 或编译因代码风格失败

构建期强制 `.editorconfig`。直接运行 `dotnet format api.slnx` 自动修复；无法自动修复的规则按提示调整代码，
不要修改 `.editorconfig` 降级。

## 变更记录

### 2026-11-09（五）：依赖合规审计与升级

**检查结论**

- 逐一核对 11 个 csproj 的 36 个 NuGet 包与 2 个 package.json 的 30 个 npm 包：
  全部直接依赖均为 MIT / Apache-2.0 宽松许可，可商业化，无 GPL / AGPL / 商业授权依赖。
- 发现两类不符合「最新稳定版」的情况：前端 vue 被 workspace overrides 锁定在 rc 预览版、
  typescript 指向 tsgo 桥接预览包，另有若干小版本落后；还有 5 组包虽非最新，但升级会破坏宽松许可
  或需要大规模迁移，详见「技术栈 → 依赖合规与版本策略」。

**升级内容**

- 后端：BCrypt.Net-Next 4.2.0 → 4.2.1、MediatR 12.4.1 → 12.5.0（最后一个 Apache-2.0 版）、
  Asp.Versioning.Mvc 8.1.0 → 8.1.1；其余已是最新稳定版。
- 前端：vue 由 rc（3.6.0-rc.14）固定为 3.5.45 稳定版，typescript 由 tsgo 桥接预览包改为 6.0.3 正式稳定版；
  axios / element-plus / playwright / jsdom / oxfmt / oxlint / sass-embedded / vite / vitest / vue-tsc 等升至最新小版本；
  根工程化 pnpm 12.14.0 → 12.18.0。移除前端 workspace 中把 Vue 生态与 TypeScript 锁死在预览版的 overrides，
  保留供应链策略配置并重新生成锁文件。
- 代码适配：MediatR 12.5.0 的请求处理委托签名新增取消令牌参数，13 处测试构造改为带参 lambda（语义不变）。

**验证**

- `dotnet build api.slnx` 0 警告 0 错误；`dotnet test api.slnx` 485/485 全通过（80/104/116/185）；
  前端 vue-tsc 类型检查通过、Vitest 146/146 全通过、无 peer 依赖冲突；
  README 类型校验与 `dotnet format api.slnx --verify-no-changes` 通过。

### 2026-11-09（四）：GitHub 不可达时仅推送 gitee

- 用户明确放行：GitHub 推送失败（代理 502 / 网络超时 / 隧道不通等）时**仅推送 gitee 即可**，
  不反复重试、不阻塞交付、不视为交付失败；只需在结果中说明 GitHub 未同步及原因，
  后续网络恢复可顺手补推但不作为待办阻塞项。
- 门禁第 3 步由「强制双端推送」调整为「以 gitee 为准，GitHub 可达时同步」；
  收尾校验由「三端 refs 一致」调整为「local / gitee 一致，GitHub 本次推送成功时再核对三端」。
- 同步更新项目长期规范记忆体与用户级记忆体；本次仅文档与规范调整，代码零改动。

### 2026-11-09（三）：固化交付门禁为项目规范

- 将「全量测试 → 更新 README（功能说明 / 使用方式 / 本次变更记录）→ 提交 → gitee + GitHub 双端推送」
  明确为**强制交付门禁**，适用范围由「开发或修复后」扩展到本仓库后续所有任务与操作：
  含依赖包（NuGet / npm）增减升级、配置 / 脚本 / 文档 / 规范调整、测试补充等。
  只要产生待提交改动即须执行，不得跳过步骤、不得调换顺序、不得只推一端，也无需另行确认。
- 补充失败处理：测试出现失败必须先修复并复跑至全绿，禁止带红提交；推送失败须重试或明确报告。
- 补充收尾校验：工作区干净、local / gitee / github 三端 refs 一致。
- 同步更新「开发规范 → 任务完成后的固定动作」章节与项目长期规范记忆体；本次仅文档与规范调整，代码零改动。

### 2026-11-09（二）：DeletedAt 软删除时间戳 + 干净迁移框架

**功能**

- 软删除由布尔标记 IsDeleted 演进为可空时间戳 `DeletedAt`（UTC）：null 表示未删除，
  非 null 同时承载「已删除」与「删除时间」；恢复即置回 null。全局查询过滤器、
  插入审计切面、字段级变更日志（记录时间戳 old / new）、令牌可用性校验、租户校验全部同步。
- **乐观并发全覆盖**：更新 / 软删除 / 恢复三条写入路径统一走 SqlSugar `ExecuteCommandWithOptLock`；
  软删除 / 恢复按列只更新 `DeletedAt`、更新审计与 `Version`，携带 `version` 时以其作 CAS 条件，
  陈旧令牌命中 0 行即抛 `40005`，成功后框架自动轮换版本令牌，杜绝旧快照复活墓碑。
  批量路径在无外层事务时自持事务保证原子性。

**迁移框架整理（不保留任何迁移历史代码）**

- 删除迁移记录实体 SchemaMigrationRecord 与 sys_schema_migration 表、基线登记、已应用记录、
  脚本指纹（SHA-256 / 旧口径自愈）、版本状态机 SchemaStatus / SchemaReadiness、
  ValidateAppliedScriptsAsync / GetStatusAsync / ValidateModelAgainstMigrationsAsync，
  以及两份历史迁移脚本（v001 岗位建表、v002 操作日志审计列）；
  `/health/ready` 与启动日志不再输出 `appliedVersion` / `expectedVersion`。
- 仅保留干净的执行通道：`ISchemaMigration`（幂等 `Scripts` + `ApplyAsync` 扩展点）、
  `SchemaMigrationCatalog`（vNNN 排序 / 冲突拒绝）、`SchemaMigrator`（事务内按序执行）、
  `DbInitializer`（缺表 CodeFirst + 执行迁移）、`ITransactionSqlRunner`、`DatabaseSchemaProbe`、
  `ModelStructureValidator`。后续新增迁移只需实现接口并在 `DependencyExtensions` 注册。
- 开发期数据库直接删除重建；为让全新库一次启动成功，宿主在结构就绪后、装载定时任务前
  显式调用 `QuartzSchemaInitializer.EnsureSchemaAsync` 幂等建立 `QRTZ_` 集群表
  （原 IHostedService 启动晚于调度器解析，会在空库上校验失败）。

**使用方式**

- 见「接口约定 → 乐观并发」（路由携带 `version`、错误码与重试策略）、
  「开发规范 → 仓储删除语义」（仓储层调用示例）与「数据库初始化」（重建流程、后续迁移扩展方式）。

**验证**

- 直接 DROP 并重建 `super_admin_test` 后，单宿主启动即完成建表 / QRTZ_ 表 / 种子；
  `dotnet test api.slnx`：Domain 80 / Application 104 / Infrastructure 116 / API 185，共 485 项全部通过；
  `dotnet format api.slnx --verify-no-changes` 与 `scripts/check-readme-types.py` 均通过。

### 2026-11-09

**功能：乐观并发全覆盖与删除通道显式分离**

- 聚合根仓储把「更新 / 软删除 / 恢复」三条写入路径统一纳入乐观并发：更新走
  `ExecuteCommandWithOptLock`，软删除 / 恢复按列更新时附加 `Version` CAS 条件并轮换版本令牌，
  冲突统一抛业务码 `40005`；版本轮换防止已删除的墓碑被旧快照的更新复活。
- 删除语义由「同名方法按实体基类隐式改写」拆成两条显式通道：`IDomainRepository<T>` 的
  `HardDeleteAsync`（物理删除）与 `IAggregateRepository<T>` 的 `SoftDeleteAsync`（软删除 / 可恢复），
  并新增按条件软删除重载；删除命令处理器与关联表替换逻辑同步改用对应入口。
- 保留并强化版本令牌：`AggregateBase` 的 `Version` 声明 IsEnableUpdateVersionValidation，
  插入时自动生成，冲突时抛 `BusinessException`（错误码 `40005`）。

**使用方式**

- 见「接口约定 → 乐观并发」（路由携带 `version`、错误码与重试策略）与
  「开发规范 → 仓储删除语义」（仓储层调用示例）。

**工程修复（本次一并纳入）**

- 种子数据：字典数据隐藏页菜单与岗位管理菜单原共用主键 109，全新库初始化时触发 MySQL 1062 主键冲突；
  字典数据隐藏页菜单改用独立主键 110，保证种子幂等。
- 集成测试夹具：MigrationsEnabled=false 路径不执行生产侧引擎修正，本机 MySQL 默认引擎为 MyISAM 时
  三张测试表不支持事务，导致工作单元回滚用例误判；夹具按 information_schema 逐表幂等修正为 InnoDB。

**验证**

- `dotnet test api.slnx`：Domain 80 / Application 104 / Infrastructure 120 / API 185，共 489 项全部通过；
- `dotnet format api.slnx --verify-no-changes`、`python3 scripts/check-readme-types.py` 均通过。

## 相关文档

- 仓库总览与前端说明：[根 README](../README.md)、[canexus-admin-vue](../canexus-admin-vue/README.md)
- 贡献流程与提交规范：[CONTRIBUTING.md](../CONTRIBUTING.md)
- 错误码与权限定义：`src/Domain.Shared/Results/ErrorCodes.cs`、`src/Domain.Shared/Permissions/PermissionDefinitions.cs`
- 配置模板：`src/API/appsettings.json`
- CI 门禁：`.github/workflows/backend-ci.yml`

## 开源许可

见仓库根目录 [LICENSE](../LICENSE)。
