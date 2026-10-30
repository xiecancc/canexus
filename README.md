# super — 通用后台管理系统

基于 **.NET 10 + Vue 3** 的前后端分离中后台管理脚手架，内置用户 / 角色 / 菜单 / 部门 / 字典 / 配置 / 公告 / 租户 / 作业调度 / 操作审计等完整 RBAC 权限体系，开箱即可二次开发。

## 项目组成

| 子项目 | 说明 | 文档 |
| --- | --- | --- |
| [api-netcore](./api-netcore/README.md) | 后端服务：.NET 10 分层架构 + SqlSugar + MySQL + Quartz | [api-netcore/README.md](./api-netcore/README.md) |
| [admin-vue](./admin-vue/README.md) | 前端应用：Vue 3 + Vite + Element Plus 管理端 | [admin-vue/README.md](./admin-vue/README.md) |

## 整体架构

```
浏览器（admin-vue，Element Plus）
    │  HTTP /api（vite dev 代理或 nginx 反向代理）
    ▼
api-netcore（http://localhost:5000）
    ├─ API 层：Controller（JWT 认证 + 权限过滤 + 操作日志）
    ├─ Application 层：业务服务 / 权限校验 / 数据范围过滤
    ├─ Domain 层：领域实体（DomainBase / AggregateBase / AggregateTreeBase）
    ├─ Infrastructure 层：SqlSugar 仓储、JWT、Quartz 调度、租户上下文
    └─ MySQL 8（CodeFirst 自动建库建表 + 种子数据）
```

## 快速开始（最小步骤）

1. 准备 MySQL 8（连接串见 [api-netcore](./api-netcore/README.md)，默认 `localhost:3306`，账号 `root/***REMOVED***`）
2. 启动后端：`cd api-netcore && dotnet run --project src/API`（首次启动自动建库建表并写入种子数据）
3. 启动前端：`cd admin-vue && pnpm install && pnpm dev`（已配置代理转发 `/api` 到后端 5000 端口）
4. 访问前端开发地址，使用种子超级管理员账号 **admin / admin123** 登录

## 约定

- 全局唯一标识符统一使用 **GUID**（36 位字符串，序列化为 `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`）
- 统一响应结构 `Result<T>`，业务错误码分段管理（见 `api-netcore/src/Domain.Shared/Results/ErrorCodes.cs`）
- 项目结构或依赖发生变更时，请同步更新本 README 与两个子项目的 README.md
