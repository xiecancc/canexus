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
- **领域事件**：事务 Outbox 持久化 + 后台 worker 重试，权限 / 字典 / 配置缓存失效由监听器驱动；
  通知公告渠道监听器按 Outbox 消息 Id 幂等去重，同一消息因其他监听器失败重投时不会重复发送渠道通知。
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
| AutoMapper | 14.0.0（MIT） | 16.3.0 | 15.0.0 起改为 Lucky Penny 商业 / RPL 双许可，需许可证密钥；14.x 官方不回补 GHSA-rvv3-g6hj-g44x（High，不受控递归 DoS；修复版仅 15.1.1 / 16.1.1 且均为商业许可，16.0.0 / 16.1.0 仍受影响）。2026-11-12 核实：触发需约 25000 层自引用对象图，请求 JSON 默认深度上限 64，常规路径不可达，故结论为「排期迁移」而非紧急修复，详见下方处置与期限表 |
| MediatR | 12.5.0（Apache-2.0） | 14.3.0 | 13.0.0 起改为商业许可；12.5.0 是最后一个 Apache-2.0 版 |
| Quartz | 3.22.9（Apache-2.0） | 4.7.2 | 4.0 仅支持 net10.0，容器构建调度器、公开 API 大面积移除，且强制 QRTZ 表结构迁移；3.x 仍在维护 |
| Asp.Versioning.Mvc | 8.1.1（MIT） | 10.2.1 | 10.x 引入 AV0016 等新诊断与配置约束，需调整版本化配置 |
| NUnit / Test.Sdk / coverlet / adapter | 4.3.2 / 17.14.0 / 6.0.4 / 5.0.0 | 5.0.0 / 18.11.0 / 10.1.0 / 6.3.1 | 5.x 改变异步断言返回值与委托签名，需重写大量测试用例；当前组合稳定 |
| Vue | 3.5.45（MIT） | 3.5.45 | 原 package.json 与 workspace overrides 锁定 rc 预览版，已移除覆盖并固定稳定版 |
| TypeScript | 6.0.3（Apache-2.0） | 7.0.2 | Volar / vue-tsc 要求 typescript < 7，7.x 移除内部 tsc 导出导致类型检查无法启动；6.0.3 为兼容线最后稳定版 |

### 依赖安全处置与期限（核实于 2026-11-12，对应评审 P1-10）

两个已披露 High 公告均经依赖审计命令与 GitHub Advisory / NuGet 核实；当前二者在常规 HTTP
请求路径均不可达，按合规项登记期限处置，不作紧急线上漏洞处理。

| 公告 | 依赖与引入路径 | 影响与修复版本 | HTTP 可达性 | 处置期限 |
| --- | --- | --- | --- | --- |
| GHSA-rvv3-g6hj-g44x / CVE-2026-32933（High，CVSS 7.5） | AutoMapper 14.0.0，Application 项目直接引用 | 深度自引用对象图（约 25000 层）触发进程级栈溢出；修复版 15.1.1 / 16.1.1 起，且 15.0 后均为商业许可；14.x 无回补 | 不可达：请求 JSON 沿用默认深度上限 64，映射源均为内部受信实体与已知 DTO，不按运行时类型名动态选择映射目标 | 2026-12-31 前完成宽松许可映射库迁移 PoC；2027-01-31 前完成全量迁移与回归 |
| GHSA-846f-f764-76mx / CVE-2026-84008（High，CVSS 7.5）及 GHSA-2fmv-rv2c-59x7（Moderate） | System.Security.Cryptography.Pkcs 8.0.1，经 SqlSugarCore 的 Oracle 驱动间接引入（**与 JWT 包无关**，原表述已更正） | 攻击者控制的 CMS 签名数据导致二次 CPU 耗尽；修复版 8.0.2 / 9.0.21 / 10.0.13 / 11.0.0-rc.2 均已发布 | 不可达：业务代码不解析 CMS / PKCS#7 数据，仅做 JWT Bearer 校验 | 2026-11-30 前显式固定该传递依赖到 10.0.14 并用依赖审计命令验证告警清零；自包含部署须用 10.0.13 及以上 SDK 重新发布 |

**补偿控制（迁移完成前）**：维持请求 JSON 默认深度上限、不映射不受信类型、不引入运行时类型名映射入口；
每次依赖变更均执行 `dotnet list package --vulnerable --include-transitive` 并确认新增告警为零。

**映射库替代评估结论**：使用面为 39 个文件 / 15 个映射配置 / 48 处映射声明 / 约 31 处映射调用 /
65 处注入引用，且依赖程序集扫描与开放泛型等专有特性，立即迁移破坏面大，故取「排期迁移」。
优先 Mapster 10.0.14（MIT，原生支持 net10.0，保留注入模型，成本最低）；备选 Mapperly 4.3.1
（Apache-2.0 而非 MIT，源生成器零运行时依赖，但需改为分部方法并重写注入模型）。

> 完整核实记录（含公告链接、依赖图、版本真实性核对与待核实清单）见本地评估文档，
> 该文件位于被忽略的 `.docs/` 目录，不纳入版本控制。

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
│   │   ├── Authorization/          # 权限特性、命令级权限启动校验、强制改密白名单特性
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
│   │   ├── Behaviors/              # 权限 / 验证 / 事务 / 数据范围 / 审计 / 幂等 / 性能
│   │   ├── Events/                 # 领域事件监听器（缓存失效、事件审计、任务调度提交后同步）
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
不会带病运行。`appsettings.json` 只写与代码默认值不同的差异项：Jwt 签发者 / 受众 / 有效期、
口令策略、登录锁定阈值、导出行数上限、缓存 TTL 与键前缀、上传扩展名白名单等默认值统一在
对应配置类（如 `PasswordPolicyOption`、`CacheOption`）中维护，部署方需要调整时再写差异项，
避免配置与代码两处重复导致值漂移。Redis 连接串以 `Cache:RedisConfiguration` 为单一来源：
缓存 L2、失效广播、分布式锁与分布式限流共用，限流未显式配置 `RateLimit:RedisConfiguration`
时自动回退到缓存连接串（`docker-compose.yml` 亦不再重复声明）。

### 主要配置项

**数据库与初始化**

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `Database:ConnectionString` | 空 | MySQL 连接串，必填 |
| `Database:AutoCreateDatabase` | `true` | 库不存在时是否用管理连接创建 |
| `Database:AutoCreateTable` | `true` | 首次初始化是否建表；Production 必须为 `false` |
| `Database:AutoSyncStructure` | `true` | `Database:AutoCreateTable` 的兼容别名（语义等价、恒以或运算并用）；Production 必须为 `false`，新配置只用 `Database:AutoCreateTable` |
| `Database:AllowSchemaInitialization` | `false` | 全新生产库首次建库建表的一次性授权 |
| `Database:MigrationsEnabled` | `true` | 是否启用迁移执行通道（按序执行全部幂等 `ISchemaMigration` 脚本） |
| `Database:ValidateModelStructure` | `false` | 启动时比对实体模型与库表列 / 非空约束 / 主键 / 列长度与类型族 / 索引覆盖（`ModelStructureValidator`），发现绕过 CodeFirst / 迁移通道的改表即阻止启动；只拦截、不自动改表 |

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
| `Cache:RedisConfiguration` | `localhost:6379` | Redis 连接串（单一来源；缓存 L2 / 失效广播 / 分布式锁 / 分布式限流共用） |
| `Cache:Hot/Medium/Cold` | 见配置文件 | 三档 TTL（本地 / 分布式秒数） |
| `Outbox:Enabled` | `true` | 领域事件 Outbox worker 开关 |
| `Outbox:PollIntervalSeconds` | `5` | 轮询间隔 |
| `Outbox:MaxAttempts` | `5` | 最大投递次数，超限转失败状态 |
| `Scheduling:ThreadCount` | `10` | Quartz 工作线程数 |
| `RateLimit:Enabled` | `true` | 限流总开关 |
| `RateLimit:GlobalPermitLimit` | `0` | 全局每窗口请求数；`0` 表示不启用，Production 必须大于 0 |
| `RateLimit:AuthPermitLimit` | `100` | 验证码 / 登录 / 刷新令牌端点配额 |
| `RateLimit:UseRedis` | `false` | 分布式限流；多副本部署必须开启 |
| `RateLimit:RedisConfiguration` | 回退 `Cache:RedisConfiguration` | 可选；仅为限流单独指定 Redis 时显式覆盖，未配置时自动回退缓存连接串 |
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
| `Bootstrap:RequirePasswordChangeOnSeed` | 必须显式 `true`（种子 admin 首登须改密） |
| `Database:AutoCreateTable` | `false` |
| `Database:AutoSyncStructure` | `false` |
| `Database:AllowSchemaInitialization` | 必须为 `false`（显式 `true` 会被拦截；全新库一次性初始化请临时改为 `true`，完成后移除） |
| `Database:MigrationsEnabled` | 必须为 `true`（显式 `false` 会让结构脱离迁移通道，仅限外部脚本全权管理结构的部署） |
| `Database:ValidateModelStructure` | `true`（启动期比对实体模型与实际列 / 非空约束 / 主键 / 列长度与类型族 / 索引覆盖，发现绕过 CodeFirst / 迁移通道的改表即阻止启动） |
| `Https:RedirectEnabled` | `true` |
| `Https:HstsEnabled` | `true` |
| `RateLimit:Enabled` 且 `RateLimit:GlobalPermitLimit` | `true` 且大于 0 |
| `MultiTenancy:Enabled` | 显式 `true` / `false` |
| `Forwarded:Enabled=true` 时 `Forwarded:KnownProxies` | 非空 |
| `Cache:RedisEnabled` | `true`（会话吊销与缓存失效依赖跨实例统一视图） |
| `RateLimit:UseRedis` | `true`（分布式限流独立开关；仅开 `Cache:RedisEnabled` 会让限流退化为进程内计数，多副本配额被放大） |

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
所有统一错误文案在返回客户端前统一经本地化器与脱敏器处理：

- zh 文化返回经脱敏的调用方原始文案（保留「不支持的文件类型：'.exe'」这类合法动态细节，
  移除 SQL 片段 / 绝对路径 / 口令赋值 / Bearer 与 JWT 令牌 / 长随机令牌串与堆栈帧，并限长 300 字符）；
- 非 zh 文化按资源表翻译（资源缺失时回退 zh-CN 资源、再回退脱敏后的原始文案）；
- 资源表静态文案为可信内容，不重复脱敏。

### 拒绝访问审计

端点级权限过滤、跨租户中间件、强制改密中间件的全部拒绝分支（未认证 401、无权限 403、
跨租户、强制改密）在返回统一错误的同时，异步写入一条 `SysOperLog` 审计记录（Title=「访问拒绝」，
含请求 URL（Query 脱敏）、IP、操作人、TraceId、错误码与脱敏后的拒绝原因）；
审计写入自身失败仅记日志，不影响业务响应。

### 权限模型

- 权限点集中声明于 `PermissionDefinitions`，命名约定 `模块:动作:资源`（如 `system:user:list`）；
- **双层校验**：控制器通过 `[RequirePermission]` 引用常量（端点级，启动期 `PermissionsStartupValidator` 反向扫描）；
  写命令通过 `[RequireCommandPermission]` / `[AllowAnonymousCommand]` / `[AllowAnyAuthenticatedCommand]` 声明策略
  （命令级，由 `PermissionBehavior` 在 MediatR 管道内执行，`CommandPermissionStartupValidator` 启动期校验漏标）。
  因此内部 `ISender.Send`、后台作业、未来新增 minimal API 等非 HTTP 入口同样受保护；端点特性降级为增强；
- 新增写命令必须显式声明三种命令策略之一，漏标会在启动期 fail-closed 阻止宿主启动（运行期对未标注命令默认放行）；
  `[RequireCommandPermission]` 的权限串同样必须已在 `PermissionDefinitions` 注册；
- 新增 HTTP 端点一律走 MVC 控制器：现存 minimal API 仅健康检查与 OpenAPI 文档，均显式匿名；
  `MinimalApiEndpointPolicyValidator` 在启动期反向扫描端点集合，新增受保护的裸 minimal 端点会阻止启动，
  堵住「新增 minimal API 绕过权限过滤器 / 强制改密」的扩展缺口；
- 超级管理员持有 `*:*:*`，在端点与命令两层均短路放行；普通角色按菜单 / 按钮授权，越权返回 403 / 40300；
- 数据范围（全部 / 自定义部门 / 本部门 / 本部门及以下 / 仅本人）在查询层统一拼接过滤条件，
  当前仅适用于**用户域**（`UserQueryPaged` 列表、`UserQueryDetail` 详情、用户域写命令准入）；
  用户列表导出复用 `UserQueryPaged` 分页口径，因此同样受数据范围约束。

### 数据范围适用边界（白名单）

数据范围是「同一租户内按部门 / 归属人收敛业务数据」的机制，**不是**租户隔离的替代品。
租户隔离由全局查询过滤器与仓储写入侧条件统一保证，对所有下表资源一律生效。
下列资源明确**不套用**用户数据范围，改由「独立权限点 + 租户隔离 + 行数上限」控制：

| 资源 | 不套用数据范围的原因 | 实际约束 |
| --- | --- | --- |
| 角色 / 菜单 / 部门 / 字典 / 岗位 / 配置 / 通知公告 / 文件 / 租户 | 租户级配置与字典数据，没有部门归属字段 | 端点与命令级权限点 + 租户全局过滤器 |
| 操作日志 / 登录日志 / 任务日志 / 实体变更日志 | 审计完整性要求按租户完整可追溯；`SysOperLog` 等实体无部门归属 / 创建人数据范围字段 | `Monitor*` 独立权限点 + 租户全局过滤器（含删除语句显式补租户条件） |
| 在线会话 / 服务器监控 / 缓存监控 | 运行态监控视图，不属于部门业务数据 | `Monitor*` 独立权限点 + 会话令牌租户归属 |
| 角色 / 操作日志 / 登录日志导出 | 复用对应分页查询口径，属平台 / 租户级审计数据 | 对应 `Monitor*` / `System*` 权限点 + `Export:MaxRows` 行数上限 + 租户全局过滤器 |

新增查询或导出接口时按此白名单判定：若资源带部门归属且属于用户域业务数据，必须接入
`IDataScopeFilter`（列表收敛 + 详情按数据范围准入校验）；否则须显式登记到上表并说明约束口径，
不得默认「无过滤」。导出一律复用对应分页查询，避免绕过既有范围 / 租户 / 行数限制。

## 数据库初始化

启动时按三层分别判定，结论写入日志与 `/health/ready` 的 `initialization` 字段（database / schema / seed）：

| 层 | 判定方式 | 行为 |
| --- | --- | --- |
| 库 | 用剥离库名的管理连接查 `information_schema.SCHEMATA` | 不存在且允许时建库，否则明确拒绝启动 |
| 结构 | 用 SqlSugar ORM 元数据（`DbMaintenance.GetTableInfoList(false)`，禁用缓存）找出缺失的模型表 | 缺表时 CodeFirst 一次性建表并修正为 InnoDB；表齐全时**不执行任何建表 DDL**。随后按序执行全部幂等迁移脚本 |
| Quartz 表 | 用 SqlSugar ORM 元数据核对 11 张 `QRTZ_*` 表是否齐全（部分表残留也能自愈补齐） | 缺表时以 `CREATE TABLE IF NOT EXISTS`（索引内联在表定义中）幂等补齐；宿主启动块内显式调用一次，不再注册宿主服务 |
| 种子 | 按稳定键幂等补齐 | 只补缺失项，不覆盖已修改数据 |

框架处于开发初期，**所有数据库都是临时的，可直接删除重建**：

```sql
DROP DATABASE IF EXISTS super_admin_test;   -- 开发 / 测试库同理
```

重建后下一次启动会依次完成建库（如允许）、CodeFirst 建表、InnoDB 引擎修正、
`QuartzSchemaInitializer` 幂等建立 `QRTZ_` 集群表、种子补齐。Quartz 建表的 11 条 DDL 全部使用
`CREATE TABLE IF NOT EXISTS` 且索引内联，MySQL 以 metadata lock 串行化并发同名建表，
多副本同时冷启动不会因 check-then-create 竞态导致第二实例启动失败。因此本框架**不保留任何迁移历史**：
没有 `sys_schema_migration` 表、没有基线登记、没有已应用记录 / 脚本指纹 / 版本状态机，
`/health/ready` 也只输出三层状态。

后续如需 CodeFirst 无法表达的结构变更（索引、约束、数据订正），扩展方式：

1. 新建 `ISchemaMigration` 实现，`MigrationId` 按 `vNNN` 递增，`Scripts` 必须是幂等 SQL
   （`CREATE TABLE IF NOT EXISTS`、`INSERT ... ON DUPLICATE KEY UPDATE` 等）；
2. 在 `DependencyExtensions` 中逐个注册 `AddSingleton<ISchemaMigration, XxxMigration>()`；
3. `SchemaMigrationCatalog` 负责排序并拒绝重复 / 非法 ID，`SchemaMigrator` 在事务内按序执行
   （原生 SQL 经 `ITransactionSqlRunner` 挂接事务；MySQL DDL 有隐式提交，故脚本必须可重复执行）。

- 生产环境禁止应用自动改表（`Database:AutoCreateTable` / `Database:AutoSyncStructure` 必须为 false）；
  两个历史开关语义等价、恒以或运算并用，初始化判定收敛到 `DatabaseOption` 的单一入口；
  新配置只用 `Database:AutoCreateTable`，另一项仅作向后兼容别名；
- 表级缺表判定与 Quartz 表完整性判定统一走 SqlSugar ORM 元数据，库级判定仍用剥离库名的
  管理连接（目标库尚未创建时业务连接不可用，不能改用 ORM 元数据）；该调整只换探测手段，
  不改变「缺表 CodeFirst 一次性建表 + 幂等迁移」的初始化语义，也不引入列同步或迁移历史；
- 全新生产库需显式设置 `Database:AllowSchemaInitialization=true` 完成一次性初始化，随后移除该项
  （稳态下生产基线校验要求其为 false）；
- `Database:ValidateModelStructure` 在生产必须为 `true`：比对实体模型与实际表结构（列、非空约束、
  主键、列长度与类型族、索引覆盖），发现绕过 CodeFirst / 迁移通道的改表即阻止启动，
  是防止手工改表破坏结构一致性的最后一道安全网；比对只做确定性规则，不自动改表。

**索引与唯一约束（SugarIndex 声明驱动）**

- 二级索引与唯一索引全部以实体上的 `[SugarIndex]` 声明为唯一事实源，由 CodeFirst 建表时一并创建：
  每张聚合表带租户 / 软删 / 创建时间复合索引（`ix_*_scope`），日志表带 CreatedAt 时间索引，
  树形表带 ParentId 索引，Outbox 带认领列索引，刷新令牌带 Token 唯一索引等（共 35 个）；
- 业务键唯一性（用户名、角色权限字符、参数键名、字典类型、岗位编码、租户标识、字典数据
  类型 + 标签 / 键值）下推为数据库唯一索引，口径与业务层查重一致（全局唯一、不含租户维度）；
- 软删除感知唯一性经聚合根基类键槽列 DeletedKey 实现：未删除行槽位恒为空串、软删除时写入
  `"#" + 主键` 墓碑标记、恢复时清回空串——唯一索引声明为 (业务键, DeletedKey)，
  使未删除集内数据库级防重、墓碑不阻断同键复用；MySQL 5.7 无函数索引，此键槽方案为
  当前版本下的等价实现；
- 应用层「先查后插」查重仍是第一道防线（模块化错误文案不变），聚合仓储在插入 / 更新 /
  恢复路径捕获 MySQL 唯一冲突（ER_DUP_ENTRY 1062）转业务错误码 CommonDataDuplicate，
  作为查重竞态窗口的并发兜底；
- 开发 / 测试库结构变更后按「删库重建」流程使索引与键槽列生效（存量库不会由启动过程补建索引）。

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

CI 分两条流水线：

- `.github/workflows/backend-ci.yml`：NuGet 还原 → 交付门禁脚本（README 类型校验 / 构建 /
  格式检查 / 全量测试 + 行覆盖率采集）→ 上传 cobertura 覆盖率产物；脚本为
  `scripts/verify-delivery.py`，与本地 `python scripts/verify-delivery.py` 同源（见
  [CONTRIBUTING.md](CONTRIBUTING.md)）。
  `services` 同时提供 `mysql:8.0` 与 `redis:7-alpine`（后者带 healthcheck），
  因为 `DistributedRateLimitApiTests` 强依赖 `localhost:6379` 且断言 Redis 可达；
- `.github/workflows/frontend-ci.yml`（前端路径变更时触发）：`pnpm install --frozen-lockfile` →
  `pnpm type-check` → `pnpm test:unit run` → `pnpm build-only`，把本地 pre-push 检查固化到 CI。

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
2. 写命令**同时**补：① `[RequireCommandPermission]` 等命令级策略标注（漏标会阻止启动）；
   ② FluentValidation 验证器（缺失会被 `WriteCommandValidatorStartupValidator` 拦截）；
3. 新增错误码同步补充 zh-CN / en-US 两份本地化资源；
4. 需要前端路由的功能，同步补充菜单种子与按钮权限；
5. 开发期数据库可直接删除重建；CodeFirst 无法表达的结构变更追加幂等 `ISchemaMigration` 脚本，禁止依赖生产环境自动改表；
6. 含令牌 / 凭据的响应实现 `ISensitiveResult`，避免被幂等管道写入 24h 缓存；
7. 定时任务命令只写库 + 发领域事件，Quartz 同步交给 `JobScheduleSynchronizationHandler`（提交后投递）；
8. 补充对应层级的测试。

### 任务完成后的固定动作（强制交付门禁）

适用范围：本仓库后续**所有**任务与操作——功能开发、Bug 修复、重构、依赖包增减或升级、
配置 / 脚本 / 文档 / 规范调整、测试补充等。只要产生待提交改动，一律按以下顺序执行：不得跳过、不得调换、
不得因「只是文档或配置改动」而省略，也无需另行确认。**推送以 gitee 为准**：GitHub 可达时同步推两端，
GitHub 推送失败时仅推送 gitee 即可。完整约定、本地环境与脚本说明见
[CONTRIBUTING.md](CONTRIBUTING.md)。

1. **全量测试**：`dotnet test api.slnx` 必须 0 失败（集成测试需有效的 `SUPER_ADMIN_TEST_CONN`，
   测试库必须是 `super_admin_test`）；失败先修复复跑，禁止带红提交；
2. **同步 README**：补充功能说明、使用方式与本次变更记录；提交前跑
   `python scripts/check-readme-types.py` 与 `dotnet format api.slnx --verify-no-changes`（本机用 `python`）；
3. **提交并推送**：`git commit -F <提交信息文件>` → `git push gitee master`；GitHub 可达时再
   `git push github master`。远端按平台命名为 `gitee` / `github`（**无 `origin`**），分支 `master`，
   默认跟踪 `gitee/master`。**GitHub 推送失败时仅推 gitee 即可**：不反复重试、不阻塞交付、
   不视为失败，说明原因即可；
4. **收尾校验**：`git status` 干净（还原 pre-push 钩子生成的前端声明文件等噪音），
   核对 local / gitee refs 一致；GitHub 本次推送成功时一并核对三端 refs。

一键脚本（与 CI 同源）：`python scripts/verify-delivery.py` 跑提交前四项校验，
`--post-push` 追加工作区与 refs 校验，`backend-ci.yml` 以 `--ci --no-restore` 调用同一脚本。

## 常见问题

### 启动报错：`Jwt:Secret` 必须显式提供

非 Development 环境不允许使用示例值或开发回退密钥。执行：

```bash
dotnet user-secrets set "Jwt:Secret" "不少于 32 字节的随机字符串" --project src/API
```

生产环境用环境变量 `Jwt__Secret` 注入。更换密钥会使全部已签发令牌立即失效。

### 启动报错：`Bootstrap:AdminInitialPassword` 必须显式提供

非 Development 环境必须提供内置超管初始口令；Development 未提供时会随机生成并在控制台输出一次。
口令需满足 `Security:PasswordPolicy`（默认 8-20 位，含字母与数字）；初始口令的长度区间
在启动期直接与 `PasswordPolicyOption` 的代码基线对齐（不依赖配置是否显式写出该节），
且不允许包含空白字符，避免内置账号口令绕过统一策略。

### Production 启动失败，列出多项「基线校验失败」

这是刻意设计的 fail-fast：按提示逐项补齐[生产环境硬性基线](#生产环境硬性基线)即可，不会逐项试错。
最常见的四项是 `Https:RedirectEnabled`、`Https:HstsEnabled`、`RateLimit:GlobalPermitLimit`
与 `RateLimit:UseRedis`（后者是独立于 `Cache:RedisEnabled` 的开关，只开缓存 Redis 仍会被拦截）。

### 忘记 admin 密码 / 登录后提示必须先改密

- 首次登录返回 `mustChangePassword=true` 属正常流程，改密前访问业务接口会返回 403 / 40302；
  调用 `POST /api/auth/change-password` 后重新登录即可；
- 忘记口令可让其他超管在用户管理中重置，或直接更新数据库中该用户的密码哈希（不推荐，需同时清理
  刷新令牌与强制改密标记）。

### 接口返回 403，提示无权限

1. 确认账号角色已勾选对应菜单 / 按钮权限；
2. 确认前端路由与按钮权限串与后端 `PermissionDefinitions` 一致；
3. 超管持有 `*:*:*`，若超管也 403，检查令牌中的角色声明与 `MultiTenancy:Enabled` 配置；
4. 多租户场景下的跨租户访问固定返回 40301；
5. 若是内部 `ISender.Send` 或后台作业调用命令，检查命令上的 `[RequireCommandPermission]` 是否与端点权限一致
   （命令级鉴权独立于端点特性生效）。

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

### 2026-11-13（十）：深度评审 P1-A2 创建 / 更新命令基类补后置钩子

1. **问题**：聚合创建与更新命令处理器的主流程为 sealed，模块无法在写入成功后插入额外步骤
   （如写关联表、补充派生数据），复杂场景只能弃用基类自行实现全流程。
2. **修复**：参照删除侧删除前守卫钩子的开放模式，创建基类新增创建后处理虚钩子（插入成功后、
   创建事件发布前执行），更新基类新增更新后处理虚钩子（乐观锁更新成功后、修改事件发布前执行，
   接收变更前快照与变更后聚合）；默认空操作，既有处理器行为不变。钩子处于命令管道事务作用域内，
   失败随事务整体回滚，关联数据就绪后再发布事件，监听器可看到完整终态。
3. **测试**：新增 2 个单测锁定钩子执行次序（校验 → 写入 → 钩子 → 发布事件）与入参语义
   （创建钩子收到与插入同一实例、更新钩子收到变更前后两份状态）；全量测试通过（601 个）。

### 2026-11-13（九）：深度评审 P1-E4 模糊检索关键字 LIKE 通配符转义

1. **问题**：列表查询的关键字经 Contains 翻译为 LIKE '%kw%'，用户输入 % 或 _ 会充当通配符
   （输入单个 % 即退化为全表匹配，放大查询成本），反斜杠与下划线也未做字面量处理。
2. **修复**：查询基类新增关键字规整方法，在纯 trim 基础上转义 LIKE 通配符（\ % _ 依次转义）；
   各列表查询的模糊检索路径统一改用该方法，等值过滤路径（字典类型、文件业务分类）保持原有
   纯 trim 语义不变。MySQL 默认（未启用反斜杠不转义模式）下参数化值内的转义序列按字面量匹配。
3. **测试**：全量测试通过（599 个），既有列表查询集成测试覆盖转义路径。

### 2026-11-13（八）：深度评审 P1-C2 校验失败响应携带字段级错误

1. **问题**：校验失败只把 FluentValidation 的错误消息拼成一句字符串，前端无法把错误定位到
   具体表单字段。
2. **修复**：统一响应新增可选 Errors 字段（属性名 → 错误消息列表）：仅校验失败响应携带，
   其余响应该字段不输出（WhenWritingNull），既有响应体结构不变；全局异常中间件按
   属性名分组填充，校验失败响应同时保留汇总 message 与字段级 errors。
3. **测试**：任务模块校验失败集成测试补 errors 字段结构断言。

**验证**：`dotnet test api.slnx` 全量通过（0 失败）。

### 2026-11-13（七）：深度评审 P1-C1 请求异常按状态码精确映射

1. **问题**：全局异常中间件把全部 BadHttpRequestException 一刀切映射为 413「上传超限」，
   而该异常类型同样承载「请求行非法 / 请求体意外结束」等 400 类问题——客户端会把
   格式错误误判为文件过大，重试语义失真。
2. **修复**：按 Kestrel 报告的状态码精确区分：413 仍映射上传超限业务码（FileSizeExceeded）；
   其余按原状态码映射并返回 CommonInvalidRequest 通用请求错误码，
   日志同步区分两类记录。

**验证**：`dotnet test api.slnx` 全量通过（0 失败）。

### 2026-11-13（六）：深度评审 P1-D2/D3 登录时序拉平与锁定双维度

1. **时序侧信道消除（D2）**：用户不存在时对固定假哈希执行一次真实 BCrypt 比对（进程内惰性
   生成假哈希），使「用户不存在」与「用户存在但密码错误」两条失败路径耗时一致，
   消除按响应时间探测账号存在性的侧信道。
2. **锁定双维度（D3）**：失败计数由「仅用户名」扩展为「用户名 + 来源 IP」双维度——
   任一维度连续失败达到上限即锁定。此前攻击者可用固定错误密码把任意已知账号打到锁定
   （定向锁定攻击）；IP 维度阻断了该路径。同一出口 IP（NAT）下的合法用户共享失败窗口，
   误锁风险由锁定窗口时长（默认 10 分钟滑动重置）与认证端点限流（auth 策略 100 次/分钟）兜底。
3. **计数实现**：双维度各自独立计数（独立分布式锁分区 + 独立滑动窗口 TTL），登录成功时
   双维度同步清除；IP 为 null（无代理直连测试环境等）时跳过 IP 维度，保持原语义。

**验证**：`dotnet test api.slnx` 全量通过（0 失败）。

### 2026-11-13（五）：深度评审 P1-B2 补齐监控 / 文件菜单与按钮种子

1. **接线 4 组已注册权限**：在线会话（`monitor:online:list` + 强制下线按钮）、服务器监控
   （`monitor:server:list`）、变更日志（`monitor:changelog:list`）、文件管理
   （`system:file:list` + 查询 / 上传 / 下载 / 删除按钮）补齐菜单与按钮种子——
   此前这些权限已注册、控制器已实现，但前端路由由 sys_menu 派生而无菜单种子，
   页面不可达（仅超管按权限直调接口）。
2. **组件回落**：在线会话与变更日志已有前端视图（`monitor/online/index`、
   `system/entity-change/index`）直接可用；服务器监控与文件管理暂无视图，
   前端路由解析自动回落「建设中」占位页，视图落地后无需再改种子。
3. 种子完成后校验「声明集 ⊆ 权限注册表」同步覆盖新增项（P1-H1 机制自动生效）。

**验证**：`dotnet test api.slnx` 全量通过（0 失败）。

### 2026-11-13（四）：深度评审 P1-H1 种子权限串收敛为单一事实源 + 启动期校验

1. **单一事实源**：菜单 / 按钮种子的全部权限标识改为引用 `PermissionDefinitions` 常量
   （约 50 处手写字符串清零），按钮种子签名从「权限前缀 + 动作」改为直接传权限常量；
   注册表 → 控制器 → 种子三处从此共用同一常量集合。
2. **孤儿权限收编**：核实 `monitor:operlog:query`、`monitor:logininfor:query`、
   `system:tenant:query` 三个按钮权限此前已随种子写入但未在注册表声明（P0-B1 同类隐患），
   补齐常量收编；语义为按钮级显隐控制，无控制器端点使用，属合法的「注册表多余项」。
3. **启动期 fail-closed 校验**：种子完成后校验「种子声明集 ⊆ `PermissionDefinitions` 注册表」，
   不合规即在启动时抛出并列出全部漂移项，防止种子串与鉴权串失配类缺陷（P0-B1）再次出现。
4. **兼容性**：种子权限串值与既有库完全一致（纠偏逻辑按名称对齐），存量库无需修数。

**验证**：`dotnet test api.slnx` 全量通过（0 失败）。

### 2026-11-13（三）：深度评审 P0-D1 登录命令退出数据库事务

1. **问题**：登录命令实现 ICommand 后被 TransactionBehavior 包裹 MySQL 事务，事务内串行执行
   验证码校验（Redis）→ 锁定检查（Redis）→ BCrypt 口令校验（CPU 50-100ms）→ 缓存操作 →
   令牌签发（refreshToken 落库）——BCrypt 把数据库连接与行锁占用放大，登录吞吐受连接池限制。
2. **修复**：写命令契约新增 `INonTransactionalCommand` 标记，TransactionBehavior 对标记命令
   不包裹事务（发布的领域事件仍排水投递）；AuthLoginCommand 标记该接口，退出事务——
   处理器主体耗时在事务外（BCrypt / Redis），唯一的数据库写入为 refreshToken 单行插入，
   登录日志走独立连接，无事务原子性需求；刷新令牌轮换保持事务语义
   （「吊销旧令牌 + 签发新令牌」需原子完成）。
3. **守卫**：Application.Tests 新增单元测试（非事务命令透放不开启事务）与架构守卫测试
   （登录命令必须保持非事务标记、刷新令牌命令必须保持事务标记），防止标记被误加或误删。

**验证**：`dotnet test api.slnx` 全量通过（Domain 92 / Application 150 / Infrastructure 152 / API 205，0 失败）。

### 2026-11-13（二）：深度评审 P0-E1/E2 全库二级索引与软删除感知唯一约束

1. **二级索引（E1）**：全部聚合表声明租户 / 软删 / 创建时间复合索引，日志表（操作 / 登录 /
   任务 / 变更日志）声明 CreatedAt 时间索引，树形表（部门 / 菜单）声明 ParentId 索引，
   Outbox 声明认领列索引，刷新令牌声明 Token 唯一 + 用户 / 会话 / 过期索引，
   用户表声明部门索引——共 35 个索引，全部由实体 `[SugarIndex]` 声明、CodeFirst 建表创建，
   消除「列表页全表扫描 + filesort」随数据量的线性劣化。
2. **唯一约束（E2）**：用户名 / 角色权限字符 / 参数键名 / 字典类型 / 岗位编码 / 租户标识 /
   字典数据（类型 + 标签、类型 + 键值）下推为数据库唯一索引，与业务层查重口径一致；
   新增聚合根基类键槽列 DeletedKey（活行空串、软删写 `"#" + 主键`、恢复清空）实现
   「软删除感知唯一」——本机 MySQL 5.7 不支持函数索引，键槽方案为等价实现；
   墓碑行不阻断同键复用，恢复撞键由唯一索引拒绝。
3. **并发兜底**：聚合仓储插入 / 更新 / 恢复路径捕获 MySQL 唯一冲突（1062）转业务错误码
   CommonDataDuplicate（40003），补齐「先查后插」查重竞态窗口的数据库级防线。
4. **测试**：新增 4 个集成用例（键槽写入 / 清空、活行重复插入转业务码、墓碑后同键复用、
   恢复撞键转业务码）；元数据收集测试随实体索引声明同步修正；测试夹具改为
   先删旧表再按当前模型重建，保证测试实体结构演进即时生效。
5. **回填方式**：索引与键槽列由 CodeFirst 建表路径创建，存量开发 / 测试库按「删库重建」
   流程生效（生产首次部署由一次性 AllowSchemaInitialization 建表路径天然携带）。

**验证**：`dotnet test api.slnx` 全量通过（含真实 MySQL 重建库后的集成测试）；
`check-readme-types.py` 与 `dotnet format api.slnx --verify-no-changes` 通过。

### 2026-11-13（一）：深度评审 P0-B1 缓存监控种子权限串修复

1. **问题**：缓存监控菜单种子的 perms 为 `monitor/cache:list`（斜杠），与权限注册表 /
   控制器要求的 `monitor:cache:list`（冒号）不一致——非超管角色即使被授予「缓存监控」
   菜单，访问 `/api/monitor/cache` 仍返回 403（种子写入的权限串与鉴权串永不相等）。
2. **修复**：`SeedDataInitializer` 的缓存监控菜单 perms 改为直接引用
   `PermissionDefinitions.MonitorCacheList` 常量，消除第二份手写字符串；
   菜单种子的原位纠偏逻辑会在既有库启动时自动把存量 `monitor/cache:list`
   纠偏为正确标识，无需手工修数。
3. **后续**：剩余菜单 / 按钮种子的权限串派生与「种子 perms ⊆ 注册表」启动校验
   在 P1-H1 批次继续收敛。

**验证**：`dotnet test api.slnx` 全量通过（Domain 92 / Application 148 / Infrastructure 148 / API 205，0 失败）。

### 2026-11-13：CRUD 样板收敛第二批（Job 命令处理器迁移基类）

1. **Job 模块迁移**：定时任务模块的 5 个标准流程写命令处理器（创建 / 删除 / 恢复 / 更新 /
   状态更新）迁移到聚合写命令处理器基类，命令契约、错误码文案、领域事件与调度同步时序
   完全不变（调度同步仍由 Outbox 提交后监听器投递）；立即执行处理器无返回值、非 CRUD 流程，
   保持显式实现。
2. **手工处理器保留结论**：逐个核实角色 / 用户 / 部门 / 菜单 / 文件 / 日志模块的手工处理器，
   确认为合法复杂编排（角色-菜单与用户-角色关联全删全插、树形挂载点移动、口令哈希旁路、
   物理文件先行上传加孤儿补偿、日志实体非聚合基），聚合基类文档本身要求此类场景显式实现；
   结论与理由登记在 `docs/CRUD样板收敛路线.md`，后续批次不再迁移。
3. **恢复泛型化暂不做**：原第三批计划对恢复处理器再抽一层泛型基类，但各模块恢复处理器
   已统一继承聚合恢复基类，逐字重复仅剩类声明与一行事件构造，收益趋近于零，明确暂不做。

**验证**：`dotnet build api.slnx` 0 警告 0 错误；`dotnet test api.slnx` 全量通过；
`dotnet format api.slnx --verify-no-changes`、`python scripts/check-readme-types.py` 均通过。

### 2026-11-12（四）：深度评审 P2 收尾（配置减负 / 结构校验 / 任务调度 / 性能与样板收敛）

本批为评审报告 P2 剩余项的收尾，遵循「不为规范化而规范化」：**开发态仍走删库重建，
不引入迁移框架、迁移历史表或自动列同步**，只收敛配置语义、改用 ORM 已有能力、补齐观测与兜底。

**配置减负（P2-4 / P2-11）**

1. **配置模板只写差异项**：`appsettings.json` 删除与代码默认值逐项一致的冗余键
   （Jwt 签发者 / 受众 / 有效期、口令策略、登录锁定阈值、导出行数上限、缓存 TTL / 键前缀 /
   负载上限、上传扩展名白名单、限流 Redis 连接串）。已逐项核对 `JwtOption`、
   `PasswordPolicyOption`、`LoginLockOption`、`ExportOption`、`CacheOption`、
   `FileStorageOption` 的代码默认值，删除后行为不变；部署方需要调整时再写差异项。
   `docker-compose.yml` 同步移除重复的限流 Redis 连接串声明。
2. **Redis 连接串单一来源**：`SecurityDependencyExtensions` 新增统一解析入口，
   `RateLimit:RedisConfiguration` 未显式配置时回退到 `Cache:RedisConfiguration`，
   使缓存 L2、失效广播、分布式锁、分布式限流共用同一 Redis；显式覆盖优先级保留。
3. **初始口令策略对齐**：`BootstrapOption` 的长度校验改为直接复用
   `PasswordPolicyOption` 的代码基线（不依赖配置节是否显式写出），并拒绝空白字符。
4. **重叠开关收敛**：`DatabaseOption` 明确 `Database:AutoSyncStructure` 是
   `Database:AutoCreateTable` 的向后兼容别名（恒以或运算并用、语义等价），
   新增统一判定入口（代码内的初始化判定属性）；
   `DbInitializer` 与 Quartz 建表改用 SqlSugar ORM 元数据（表清单探测禁用缓存）替代手写
   information_schema 查询，库级判定仍保留剥离库名的管理连接。架构测试白名单同步去掉
   已不再使用裸 Ado 的 `DatabaseSchemaProbe` 条目。**不改变初始化语义、不引入列同步**。

**结构校验与调度（P2-6 / P2-7 / P2-8）**

5. **模型结构校验补强**：`ModelStructureValidator` 在原列存在性 / 非空约束 / 主键比对之外，
   增加确定性的列长度、类型族、小数位精度与「模型声明索引是否被覆盖」比对；
   纯比对逻辑抽出为可单测方法，新增 `ModelStructureValidatorTests` 覆盖差异与误报边界。
   刻意不解析 MySQL 类型别名文本，避免 `int(11)` 之类误报；只拦截漂移、不自动改表。
   Aop 对 CopyNew / SetColumns 写入路径的覆盖经调研判定为高风险低收益，本批**只给结论不改代码**。
6. **任务调度同步兜底**：`JobScheduleManager` 在单任务装载的异常被吞掉后复核调度器实际任务数，
   对缺失任务逐个重新同步并记录失败告警；`JobExecuteWrapper` 补齐任务级异常日志。
   MySQL 高可用仅给出运维结论，不在本批引入代码改动。
7. **缓存监控降级可重试**：`CacheMonitorProvider` 改为按 `CacheOption` 统一建连参数、
   异步懒加载，连接失败不缓存永久失败状态，后续请求可重试；监控端点始终可响应，
   缓存故障告警仍由健康检查承担。

**性能、整洁与样板（P2-9 / P2-1 / P2-3 / P2-10）**

8. **三项低风险性能 / 整洁改动**（P2-9）：最新公告查询下推分页与排序后取前 N 条，
   角色菜单授权改为批量插入，文件上传元数据写库失败时补偿删除已落盘孤儿文件；
   新增 `ApplicationHardeningTests` 覆盖。日志实体抽公共基类、树形查询结果缓存
   经调研判定为高风险 / 收益不明，本批**只给结论不改代码**。
9. **控制器版本常量联动**（P2-1）：20 个控制器统一由硬编码版本号改为引用
   `ApiVersioningExtensions.CurrentVersion`，破坏性版本发布时只需改一处；
   `ApiVersioningExtensions` 注释同步修正。新增 `docs/CRUD样板收敛路线.md`，
   记录现状、暂不做全量泛型基类 / Source Generator 的理由与按模块分批路线，
   明确「明确不做」清单（不重写充血聚合、不移 SqlSugar 特性、不引入 ABP / EF Core / Auto API）。
10. **CI 与文档漂移**（P2-3 / P2-10）：README 的后端 CI 描述改为「NuGet 还原 → 交付门禁脚本
    （README 类型校验 / 构建 / 格式 / 全量测试 + 覆盖率采集）→ 上传 cobertura 产物」，
    并链接 `CONTRIBUTING.md`；相关文档补 `docs/CRUD样板收敛路线.md` 与
    `docs/事务语义与ORM选型说明.md`。

**明确不做**（沿用评审 P3 结论）：不为规范化重写充血聚合、不引入迁移框架 / 自动列同步、
不做控制器全量泛型化、不统一文件下载 / 健康检查响应包装。

**验证**：`dotnet build api.slnx` 0 警告 0 错误；`dotnet test api.slnx` 全量 593 项通过
（Domain 92 / Application 148 / Infrastructure 148 / API 205）；`dotnet format api.slnx
--verify-no-changes`、`python scripts/check-readme-types.py` 均通过。本批无前端源码改动，
未重跑前端流水线。

### 2026-11-12（三）：深度评审 P1 收尾 + P2 首批（多租户 / 数据范围 / 权限版本号 / 依赖安全 / 前端 / 门禁）

**P1 剩余 4 项**

1. **P1-2 多租户口径统一**：`AggregateRepository.GetDeletedAsync` 改按请求租户上下文
   （`tenantContext.TenantId`）而非 JWT 声明约束，堵住平台用户带 X-Tenant-Id 跨租户读墓碑；
   `SqlSugarClientFactory` 为「平台态租户过滤整体短路」加一次性结构化告警审计；
   新增 `PlatformWideOperationWhitelist` 登记 3 处平台级全表操作（日志保留 / 刷新令牌清理 /
   令牌版本递增，业务语义【待核实】），三处补操作名 + 影响行数日志，**不改变业务语义**。
2. **P1-4 数据范围白名单文档化**：新增「数据范围适用边界」章节，明确数据范围仅适用于用户域
   （含用户导出，因其复用分页口径）；角色 / 菜单 / 部门 / 字典 / 日志 / 在线会话 / 监控 /
   角色与日志导出改由独立权限点 + 租户隔离 + 行数上限控制，并给出新增资源的判定规范。
3. **P1-6 权限版本号防复活**：`PermissionChecker` 的全局版本号缓存 key 丢失时
   （Redis 重启 / flush / 淘汰 / 首次冷启动）不再回退 0，改为重建以毫秒时间戳为基线、
   进程内严格单调的安全版本号并写回，历史 v0 用户权限 key 永不复活；
   新增 6 个单测覆盖 miss 重建、时钟回拨、并发与旧 key 失效。
4. **P1-10 依赖安全处置与期限**：核实两个 High 公告（AutoMapper 14 不受控递归 DoS、
   System.Security.Cryptography.Pkcs 8.0.1 CMS 二次 CPU 耗尽），更正传递依赖引入路径
   （来自 SqlSugarCore 的 Oracle 驱动，与 JWT 包无关）；二者在常规 HTTP 路径均不可达，
   按合规项登记补偿控制与明确期限；完成 MIT 映射库替代评估（优先 Mapster，结论「排期迁移」）。
   完整核实记录见本地评估文档（`.docs/`，不提交）。

**P2 首批（按优先级择低成本高价值项）**

5. **P2-2 前端能力闭环**：补齐操作日志 / 登录日志 Excel 导出 API 与页面按钮；新增在线用户、
   岗位管理、实体变更日志 3 个视图与对应 API / 单测；服务器监控、缓存监控、文件管理
   列入前端 README 的 Roadmap（含缺失内容与待补原因）。
6. **P2-3 覆盖率与架构守护**：新增 `scripts/verify-delivery.py` 的 `--coverage` 采集与
   行覆盖率摘要（可配 `--min-line-rate` 硬门禁），CI 上传 cobertura 产物；
   架构测试白名单的语义依据文档 `docs/事务语义与ORM选型说明.md` 入库，
   修复「引用不存在的 `.docs/` 文件导致理由链断裂」。
7. **P2-5 交付门禁脚本化**：新增 `scripts/verify-delivery.py`（README 类型校验 / 构建 /
   格式 / 全量测试 / 工作区干净 / refs 一致），`backend-ci.yml` 改为调用同一脚本；
   运维与推送约定移至 `CONTRIBUTING.md`，README 门禁章节精简为摘要 + 链接。
8. **P2-10 过时注释修正**：修正 `Program.cs` 中间件顺序自相矛盾的注释、
   `GlobalExceptionHandlingMiddleware` 的「资源段 404」死分支描述、
   部门树「祖级路径」早期设计残留（接口 / 仓储 / 验证器 / 树形仓储注释统一），
   并在 `ApiVersioningExtensions` 标注控制器硬编码版本常量待收敛。

**验证**：`dotnet build api.slnx` 0 警告 0 错误；全量测试通过；
前端 `pnpm type-check` / Vitest / `build-only` 全绿；README 类型校验与格式校验通过。

### 2026-11-12（二）：深度评审 P1 批次（拒绝审计 / 本地化脱敏 / Outbox 通知幂等 / 生产基线 / 中央构建）

1. **拒绝访问审计（P1-1）**：新增 `AuthorizationRejectionAudit` 静态辅助类，权限过滤器（未认证 / 无权限 / 默认拒绝
   4 个分支）、跨租户中间件（4 个拒绝分支）、强制改密中间件的全部拒绝路径异步写 `SysOperLog`
   （Title=「访问拒绝」，URL Query 脱敏、原因脱敏截断）；写入失败仅记日志，不外泄异常。
2. **MediatR 管道顺序修正（P1-7）**：日志 / 性能监控 Behavior 上移到最外层，
   校验失败、越权拒绝、幂等命中等短路请求同样被性能统计与日志覆盖；
   `PerformanceBehavior` 补 try/finally，异常路径也输出慢请求告警；
   `AuditLogBehavior` 补有界重试（最多 2 次）与最终失败告警，并经 `IOperationContext`
   新增的 HTTP 元数据字段记录真实 Method / Path / IP。
3. **错误文案本地化 + 脱敏（P1-3）**：统一响应层全部错误出口（异常中间件、限流、请求体限制、
   权限过滤器、跨租户 / 强制改密中间件、启动期验证）接入 `ErrorMessageLocalizer`；
   新增 `ErrorMessageSanitizer` 对调用方原始文案统一脱敏（SQL / 路径 / 口令 / 令牌 / 堆栈帧，
   限长 300，幂等可重入），zh 文化不再原样回吐异常消息，en-US 资源覆盖 69 个错误码。
4. **Outbox 通知幂等去重（P1-5）**：`DomainEventNotification` 增加可选 Outbox 消息 Id
   （由 `OutboxDeliveryService` 传入），`NoticeChannelDispatcher` 按消息 Id 缓存去重标记（24h），
   同一 Outbox 消息因其他监听器失败重投时不再重复发送公告渠道通知。
5. **生产基线加固（P1-8）**：`ProductionConfigurationValidator` 新增 4 项校验——
   `Bootstrap:RequirePasswordChangeOnSeed` 必须显式 true；`Database:AllowSchemaInitialization`
   稳态必须 false；`Database:MigrationsEnabled` 必须为 true；`Database:ValidateModelStructure`
   必须为 true（启动期模型 / 库表比对作为防手工改表的安全网）。
6. **中央构建文件（P1-9）**：新增 `Directory.Build.props` 集中管理 11 个项目的 6 项共有属性
   （TargetFramework / TreatWarningsAsErrors / EnforceCodeStyleInBuild / GenerateDocumentationFile /
   Nullable / ImplicitUsings），各 csproj 仅保留特有属性；API.csproj 补 3 个显式 ProjectReference。
   依赖版本零变更（OpenTelemetry 混用 1.20.1 / 1.20.2 属 NuGet 上游可用版本差异，维持现状）。

**验证**：`dotnet build api.slnx` 0 警告 0 错误；`dotnet test api.slnx` 全量通过；
新增用例覆盖脱敏器、拒绝审计、性能异常路径、通知去重、生产基线新校验。

### 2026-11-12：深度评审 P0 实害修复（限流 / Quartz / 幂等 / CI / 命令级鉴权 / 调度一致性 / 错误响应）

**功能与使用方式变化**

1. **生产限流开关对齐**：`ProductionConfigurationValidator` 在 Production 额外强制
   `RateLimit:UseRedis=true`（此前只校验 `Cache:RedisEnabled`，而限流是否走 Redis 由独立开关决定）；
   `appsettings.json` 的 RateLimit 配置节显式补出 UseRedis / RedisConfiguration / RedisKeyPrefix 三个键。
   非 compose 部署必须同时提供这两个开关，否则启动即被拒绝，避免多副本配额被放大。
2. **Quartz 建表并发安全**：`QuartzSchemaInitializer` 改为静态唯一入口，从宿主服务注册中
   移除（消除一次启动两处调用）；11 条 DDL 改为 `CREATE TABLE IF NOT EXISTS` 且索引内联，
   存在性判定由「仅看 `QRTZ_LOCKS`」改为「11 张表齐全」，中途失败留下部分表时可自愈补齐。
3. **幂等正确性优先**：`IdempotencyBehavior` 拿不到锁不再降级无锁执行，改为抛 40005 拒绝；
   持锁期间写 in-flight 占位（2 分钟）防止锁过期后并发穿透，处理器失败时清除占位以允许重试；
   含令牌的响应实现 `ISensitiveResult`（当前为 `TokenPairDto`），整条幂等路径跳过，JWT 不再写入 24h L2 缓存。
4. **命令级鉴权**：新增 `RequireCommandPermissionAttribute` / `AllowAnonymousCommandAttribute` /
   `AllowAnyAuthenticatedCommandAttribute`，62 个写命令逐一声明策略；`PermissionBehavior` 位于审计之后、
   验证之前，`CommandPermissionStartupValidator` 启动期校验漏标与权限注册（fail-closed）。
   端点特性保留为增强层，内部 `ISender.Send` 与非 HTTP 入口也受保护；
   同时新增 `MinimalApiEndpointPolicyValidator`，启动期反向扫描非 MVC 端点，
   强制「新端点一律走控制器或显式匿名」，堵住新增 minimal API 绕过权限 / 强制改密的扩展缺口。
5. **任务调度提交后同步**：5 个任务命令处理器移除事务内的 Quartz 调用，只写库 + 发领域事件；
   新增 `JobScheduleSynchronizationHandler` 监听 Outbox 提交后投递的事件完成调度同步
   （失败由 Outbox 重试机制补偿），删除顺序自然变为「先软删提交、再移出调度」；
   `JobRunOnceCommandHandler` 保持原样。
6. **错误响应保留跨切头 + 请求日志顺序**：`UnifiedResponseWriter` 在 `Response.Clear()` 前暂存并恢复
   Access-Control-* / Strict-Transport-Security / Vary / X-Trace-Id 响应头；Serilog 请求日志中间件
   上移到异常中间件外层，异常请求的结构化日志状态码不再是 200。
7. **CI 补齐**：`backend-ci.yml` 增 `redis:7-alpine` service（带 healthcheck）；新增
   `frontend-ci.yml` 跑前端类型检查 / Vitest / 生产构建。

**验证**：`dotnet build api.slnx` 0 警告 0 错误；`dotnet test api.slnx` 全量通过；
新增用例覆盖配置基线、命令权限、幂等锁 / 占位 / 敏感响应、调度同步事件、命令权限覆盖率。

### 2026-11-09（六）：远端 `origin` 重命名为 `github`

- 为与 `gitee` 保持一致的平台化命名，执行 `git remote rename origin github`：
  远端分别为 `gitee` 与 `github`，`master` 仍默认跟踪 `gitee/master`，
  原 `refs/remotes/origin/*` 自动迁移为 `refs/remotes/github/*`。
- 推送命令由 `git push origin master` 改为 `git push github master`；本文件门禁、项目级与用户级
  记忆体同步更新。`origin` 不再保留为别名，避免同名混淆；若第三方工具强依赖 `origin`，
  可另行执行 `git remote add origin https://github.com/xiecancc/canexus.git`。
- 仅本地 `.git/config` 变更（不随 clone 传播），代码、依赖与 CI 配置无改动。

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
- CRUD 样板收敛路线：`docs/CRUD样板收敛路线.md`
- 事务语义与 ORM 选型：`docs/事务语义与ORM选型说明.md`
- CI 门禁：`.github/workflows/backend-ci.yml`

## 开源许可

见仓库根目录 [LICENSE](../LICENSE)。
