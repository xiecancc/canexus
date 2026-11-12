# 贡献与交付约定

本文件沉淀仓库的**运维与交付约定**（原置于 `canexus-api-netcore/README.md` 的门禁章节），
面向贡献者与自动化流程；功能说明、配置表与接口清单仍以后端 README 为准。

## 仓库结构

- `canexus-api-netcore/` — .NET 10 后端（`api.slnx`，11 个项目，DDD 分层）。
- `canexus-admin-vue/` — Vue 3 + TypeScript 前端（pnpm workspace）。
- `.github/workflows/` — `backend-ci.yml`（后端回归门禁）与 `frontend-ci.yml`（前端类型检查 / 单测 / 构建）。
- 根 `package.json` / `.husky/` / `.lintstagedrc.mjs` — 提交前格式与类型检查钩子。

## 本地开发环境

| 依赖 | 说明 |
| --- | --- |
| .NET SDK 10.0.x | `dotnet build api.slnx` |
| Python 3.x | 本机用 `python`（Windows 商店占位程序 `python3` 不可用）；CI 的 ubuntu-latest 用 `python3` |
| MySQL 8.x（localhost:3306） | 集成测试库固定为 `super_admin_test`，**禁止**指向 `super_admin` |
| Redis（localhost:6379） | 分布式限流集成测试断言 Redis 必须可达 |
| Node 26 + pnpm 12.18.0 | 前端 `pnpm install --frozen-lockfile` |

集成测试连接串经环境变量 `SUPER_ADMIN_TEST_CONN` 注入，格式示例：

```text
server=localhost;port=3306;database=super_admin_test;user id=root;password=<本地开发口令>;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none
```

开发态领域实体变更一律走「删库重建」：`DROP DATABASE super_admin_test` 后重启，
由缺表路径的 CodeFirst 全量重建表结构，**不需要迁移、不需要列同步**；生产态禁止应用自动改表，
由外部脚本初始化（详见后端 README「数据库初始化」章节）。

## 编码规范

- 11 个项目全部启用 `TreatWarningsAsErrors` 与 `EnforceCodeStyleInBuild`；统一格式 `dotnet format api.slnx`。
- 行尾 CRLF（`.editorconfig`）；公共成员必须有 XML 注释（CS1591 经 `GenerateDocumentationFile` 提升为错误）。
- 新增权限必须在 `PermissionDefinitions` 声明；新增写命令必须配验证器与命令级策略标注；
  新增错误码必须补 zh-CN / en-US 两份本地化资源。
- README 中反引号内的 PascalCase 标识符必须是仓库真实 C# 类型，由 `scripts/check-readme-types.py` 校验。

## 强制交付门禁

**任何任务只要产生待提交改动**（功能开发、Bug 修复、重构、依赖增减或升级、配置 / 脚本 /
文档 / 规范调整、测试补充等）都必须按下列顺序走完，不得跳过、不得以「只是文档改动」为由省略：

1. **全量测试**：`dotnet test api.slnx`，必须 0 失败（Domain / Application / Infrastructure / API 四组）。
   失败先修复并复跑至全绿，禁止带红提交。
2. **同步 README**：更新 `canexus-api-netcore/README.md` 的功能说明、使用方式与本次变更记录；
   任何影响安装、配置、接口、目录结构、依赖版本、运维方式或开发规范的改动都必须同步。
   提交前跑 `python scripts/check-readme-types.py` 与 `dotnet format api.slnx --verify-no-changes`，两项均需通过。
3. **提交并推送**：`git add` + `git commit`（提交信息用 `-F` 文件，避免 shell here-string 出错），
   推送 `git push gitee master`；GitHub 可达时再执行 `git push github master` 保持双端同步。
   远端按平台命名为 `gitee` / `github`（**不再有 `origin`**），分支为 `master`，默认跟踪 `gitee/master`。
   **GitHub 推送失败（代理 502 / 网络超时等）时仅推送 gitee 即可**：不反复重试、不视为交付失败、
   不阻塞收尾，只需在结果中说明原因；网络恢复后可顺手补推，但不作为阻塞项。
4. **收尾校验**：确认 `git status` 干净（还原 pre-push 钩子生成的
   `canexus-admin-vue/src/auto-imports.d.ts` 等噪音），核对 local / gitee refs 一致；
   GitHub 本次推送成功时一并核对三端 refs。

### 一键脚本

`scripts/verify-delivery.py` 固化了上述可自动验证的部分：

```bash
# 本地：提交前（README 类型校验 + 构建 + 格式校验 + 全量测试）
python scripts/verify-delivery.py

# 本地：推送后追加（工作区干净 + refs 一致）
python scripts/verify-delivery.py --post-push

# CI：由 backend-ci.yml 调用，跳过工作区 / refs 检查
python3 scripts/verify-delivery.py --ci --no-restore
```

脚本只做校验，**不改文件、不提交、不推送**；格式化需显式运行 `dotnet format api.slnx`。

## 提交信息

- 使用 `git commit -F <文件>`，避免 Bash here-string 在 Windows 上的转义问题。
- 摘要一行概括批次，正文按项列出改动与验证结果（测试数量、格式 / 类型校验结果）。
