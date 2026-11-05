# 贡献指南

感谢关注本项目！欢迎通过 Issue 反馈问题、提交 Pull Request 参与贡献。

## 开发环境

| 依赖 | 版本要求 |
| --- | --- |
| Node.js | >= 26 |
| pnpm | 12.x（根目录 `package.json` 已通过 `packageManager` 约束） |
| .NET SDK | 10.0 |
| MySQL | 8.x |

首次克隆后：

```bash
# 1. 根目录安装依赖（激活 husky 提交钩子）
pnpm install

# 2. 启动后端（首次启动自动建库建表并写入种子数据）
dotnet run --project canexus-api-netcore/src/API

# 3. 启动前端
cd canexus-admin-vue && pnpm install && pnpm dev
```

详见根 [README.md](./README.md) 与各子项目 README。

## 提交规范

提交信息必须符合 **Conventional Commits** 格式（`.husky/commit-msg` 钩子强制校验，不符合将拒绝提交）：

```
type(可选范围): 描述
```

允许的 type：

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 缺陷修复 |
| `docs` | 文档变更 |
| `style` | 代码格式（不影响逻辑） |
| `refactor` | 重构（既非新增也非修复） |
| `perf` | 性能优化 |
| `test` | 测试相关 |
| `build` | 构建 / 依赖变更 |
| `chore` | 其他杂项 |
| `revert` | 回滚提交 |

示例：`refactor: 统一更新审计口径为可空语义`、`feat(api): 引入领域事件机制`

## 提交与推送门禁

- **pre-commit**：前端 `oxlint --fix` + `oxfmt`、后端 `dotnet format`（lint-staged 自动修复并重新暂存，无法修复的问题会中断提交）
- **pre-push**：前端类型检查（vue-tsc）、Vitest 单元测试、后端编译检查

请在提交前确保本地检查全部通过。

## Pull Request 流程

1. 从 `master` 拉出功能分支（如 `feat/domain-event-retry`）
2. 开发并提交，遵循上述提交规范
3. 同步更新受影响的文档（根 README 与子项目 README，见根 README「约定」）
4. 发起 PR，填写模板中的各项内容，确保本地检查全部通过
5. 等待 Review，按意见修改后重新推送

## 反馈问题

提交 Issue 前请先搜索已有 Issue 避免重复。缺陷报告请附上复现步骤、期望行为、实际行为与环境信息（使用 Issue 模板填写）。
