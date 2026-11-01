# Canexus（灿枢）— 仿若依风格的 .NET 全家桶后台管理系统

基于 **.NET 10 + Vue 3** 的仿若依（RuoYi）风格前后端分离管理系统，内置用户 / 角色 / 菜单 / 部门 / 字典 / 配置 / 公告 / 租户 / 作业调度 / 操作审计等完整 RBAC 权限体系，开箱即可二次开发；规划中：工作流（内置模块）、手机端（canexus-app）、小程序（canexus-mini）。

## 项目组成

| 子项目 | 说明 | 文档 |
| --- | --- | --- |
| [canexus-api-netcore](./canexus-api-netcore/README.md) | 后端服务：.NET 10 分层架构 + SqlSugar + MySQL + Quartz | [canexus-api-netcore/README.md](./canexus-api-netcore/README.md) |
| [canexus-admin-vue](./canexus-admin-vue/README.md) | 前端应用：Vue 3 + Vite + Element Plus 管理端 | [canexus-admin-vue/README.md](./canexus-admin-vue/README.md) |

## 整体架构

```
浏览器（canexus-admin-vue，Element Plus）
    │  HTTP /api（vite dev 代理或 nginx 反向代理）
    ▼
canexus-api-netcore（http://localhost:5000）
    ├─ API 层：Controller（JWT 认证 + 权限过滤 + 操作日志）
    ├─ Application 层：业务服务 / 权限校验 / 数据范围过滤
    ├─ Domain 层：领域实体（DomainBase / AggregateBase / AggregateTreeBase）
    ├─ Infrastructure 层：SqlSugar 仓储、JWT、Quartz 调度、租户上下文
    └─ MySQL 8（CodeFirst 自动建库建表 + 种子数据）
```

## 快速开始（最小步骤）

1. 准备 MySQL 8（连接串见 [canexus-api-netcore](./canexus-api-netcore/README.md)，默认 `localhost:3306`，账号 `root/***REMOVED***`）
2. 启动后端：`cd canexus-api-netcore && dotnet run --project src/API`（首次启动自动建库建表并写入种子数据）
3. 启动前端：`cd canexus-admin-vue && pnpm install && pnpm dev`（已配置代理转发 `/api` 到后端 5000 端口）
4. 访问前端开发地址，使用种子超级管理员账号 **admin / admin123** 登录

> **⚠️ 安全提示**：仓库内置的数据库账号（`root/***REMOVED***`）、种子管理员密码（`admin/admin123`）与 JWT Secret（见 `appsettings.json`）**仅为本地开发默认值**。部署到任何非本地环境前，必须全部替换为强口令，并通过环境变量或生产配置覆盖 `Database:ConnectionString` 与 `Jwt:Secret`，切勿将生产密钥提交进仓库。

## 工程化规范

- **Git Hooks（husky + lint-staged，仓库根级）**：根目录 `package.json` 持有 husky / lint-staged，`.husky/` 与 `.git` 同级，钩子覆盖全部子项目——提交前前端文件 `oxlint --fix` + `oxfmt`、后端 `.cs` 文件 `dotnet format`；推送前执行前端类型检查（vue-tsc）、Vitest 单元测试与后端编译检查。克隆后先在根目录执行一次 `pnpm install` 激活钩子（`prepare` 脚本配置 `core.hooksPath`），再进各子项目安装依赖。配置见根 `package.json` 与 `.lintstagedrc.mjs`。
- **CI 流水线（GitHub Actions）**：`.github/workflows/ci.yml`，push 与 PR 触发——前端 Job（lint / 类型检查 / 单测 / 构建）与后端 Job（dotnet build / test，MySQL 8 服务容器承载集成测试）并行执行。

## 约定

- 全局唯一标识符统一使用 **GUID**（36 位字符串，序列化为 `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`）
- 统一响应结构 `Result<T>`，业务错误码分段管理（见 `canexus-api-netcore/src/Domain.Shared/Results/ErrorCodes.cs`）
- 项目结构或依赖发生变更时，请同步更新本 README 与两个子项目的 README.md

## 参与贡献

欢迎提交 Issue 与 Pull Request，流程与提交信息规范见 [CONTRIBUTING.md](./CONTRIBUTING.md)；社区行为准则见 [CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md)。

## 开源许可

本项目基于 [MIT License](./LICENSE) 开源，使用或分发时请保留许可证与版权声明。
