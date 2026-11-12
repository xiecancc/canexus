# canexus-admin-vue — 管理端前端

中后台管理系统的 Web 前端，基于 Vue 3 + Vite + Element Plus，内置动态路由（按后端菜单动态生成）、RBAC 按钮级权限指令、Pinia 状态管理与完整的工作区工程化配置（类型检查 / 单测 / E2E / Lint / Format）。

## 技术路线

| 分类 | 技术 | 版本 |
| --- | --- | --- |
| 框架 | Vue（组合式 API，`<script setup>`） | 3.x（rc） |
| 构建 | Vite（+ vite-plugin-vue-devtools） | 8.3.5+ |
| 语言 | TypeScript（tsgo 原生预览，经 typescript-native-bridge 桥接） | 6.0 / 7.0 预览 |
| UI 组件 | Element Plus（按需自动导入）+ @element-plus/icons-vue | 2.14.7 / 2.3.2 |
| 状态管理 | Pinia | 4.1.0 |
| 路由 | Vue Router | 5.4.0 |
| HTTP | Axios | 1.20.0 |
| 工具库 | @vueuse/core | 15.0.0 |
| 代码质量 | oxlint / oxfmt | 1.89 / 0.74 |
| 自动导入 | unplugin-auto-import / unplugin-vue-components | 21.x / 32.x |
| 单元测试 | Vitest + @vue/test-utils（jsdom） | 5.0.5 / 2.5.1 |
| E2E 测试 | Playwright | 1.64.1 |
| 样式 | sass-embedded | 1.106.1 |

> Node 版本要求 **>= 26**，包管理器使用 **pnpm**。

## 功能模块

| 模块 | 路径 | 说明 |
| --- | --- | --- |
| 登录 | `views/login` | 账号密码 + 验证码，JWT 令牌管理（无感刷新） |
| 工作台 | `views/dashboard` | 首页概览 |
| 系统管理 | `views/system` | 用户 / 角色 / 菜单 / 部门（树形）/ 字典 / 参数配置 / 通知公告 / 租户 / 岗位 / 实体变更日志 / 定时任务（含执行日志抽屉） |
| 系统监控 | `views/monitor` | 操作日志、登录日志（均支持 Excel 导出）、在线会话（强制下线） |
| 错误页 | `views/error` | 403 / 404 等 |
| 通用能力 | `components` `composables` `directives` | 字典标签、权限指令、布局、动态路由等 |

## Roadmap：后端能力前端视图对齐（P2-2）

> 代码评审 P2-2 处置记录。后端 `canexus-api-netcore` 提供 6 个能力而前端一度无对应视图 / API。
> 处置原则：能以「列表 + 分页 + 基础操作」与现有 `usePage` + `el-table` + `v-permission` 约定同构补齐的，直接补视图与 `src/api`；
> 成本明显偏高、接口语义为只读快照（非分页列表）或无菜单入口的，**不写空壳占位页**，登记在此并说明原因。
> 动态路由由后端菜单驱动（`src/router/dynamic.ts`）：组件映射失败会回落「建设中」占位页；
> 菜单种子未登记 component 的视图不会被路由到，需后端同步补菜单（见下表「菜单状态」）。

### 已补齐

| 能力 | 前端视图 | API 模块 | 后端端点 | 菜单状态 |
| --- | --- | --- | --- | --- |
| 岗位管理 | `views/system/post/index.vue`（含 `components/PostFormDialog.vue`） | `api/system/post.ts` | `GET /api/system/post/page`、`/{id}`、`POST`、`PUT /{id}/{version}`、`DELETE /{id}/{version}` | 种子已登记 `system/post/index`（补齐前回落「建设中」） |
| 实体变更日志 | `views/system/entity-change/index.vue` | `api/system/entitychange.ts` | `GET /api/system/entity-change/page` | 种子未登记，建议补 `system/entity-change/index` |
| 在线用户 | `views/monitor/online/index.vue` | `api/monitor/online.ts` | `GET /api/monitor/online/page`、`POST /api/monitor/online/{tokenId}/revoke` | 种子未登记，建议补 `monitor/online/index` |
| 日志 Excel 导出 | `views/monitor/operlog`、`views/monitor/logininfor`（新增「导出」按钮） | `api/monitor/operlog.ts` 的 `exportOperLogApi`、`api/monitor/loginlog.ts` 的 `exportLoginLogApi` | `GET /api/system/oper-log/export`、`GET /api/system/login-log/export`（xlsx 文件流，经 `utils/download.ts` 以 blob 下载） | 菜单已含 `monitor:operlog:export` / `monitor:logininfor:export` 按钮权限 |

### 待补齐（Roadmap）

| 优先级 | 能力 | 缺失内容 | 后端端点 | 待补原因 |
| --- | --- | --- | --- | --- |
| 中 | 文件管理 | 上传（multipart）+ 分页列表 + 文件流下载 + 删除的完整闭环 | `POST /api/system/file/upload`、`GET /api/system/file/page`、`GET /api/system/file/{id}/download`、`DELETE /api/system/file/{id}/{version}` | 涉及 `el-upload` 上传交互、blob 下载与失败重试，明显比标准 CRUD 复杂；且菜单种子未登记该菜单，补了也无入口。待菜单与上传 / 下载交互规范确定后按 `system/file/index` 补齐，不为凑数写半套页面 |
| 低 | 服务器监控 | 运行指标快照展示页 | `GET /api/monitor/server`（`ServerMonitorResponse`：CPU / 内存 / GC / 线程 / 句柄 / 调度器概要） | 单个 GET 返回只读快照而非分页列表，需专门的指标卡片 / 仪表盘布局，与现有 `usePage + el-table` 约定不同构，成本偏高 |
| 低 | 缓存监控 | 缓存指标与 TTL 档位展示页 | `GET /api/monitor/cache`（`CacheMonitorResponse`：Redis 连接状态、命中率、DBSIZE、TTL 三档嵌套等） | 同为只读快照 + 嵌套结构，需专门布局与数值 / 可空字段格式化；菜单种子虽已登记 `monitor/cache/index`（当前回落「建设中」），但不适合用列表页模板套用 |

## 目录结构

```
canexus-admin-vue/
├── index.html                      # 入口 HTML
├── vite.config.ts                  # Vite 配置（/api 代理转发后端）
├── .env / .env.production          # VITE_API_BASE_URL、VITE_PROXY_TARGET
├── e2e/                            # Playwright E2E 测试
├── src/
│   ├── main.ts                     # 应用入口
│   ├── App.vue
│   ├── api/                        # 接口层：auth、system/*、monitor/* 与 TS 类型定义
│   ├── router/                     # 路由（静态 + 按后端菜单动态生成）
│   ├── stores/                     # Pinia（认证、权限等）
│   ├── views/                      # 页面：dashboard / login / system / monitor / error
│   │   └── system/                 # user/role/menu/dept/dict/config/notice/tenant/job
│   ├── components/                 # 通用组件（字典标签、表单对话框等）
│   ├── composables/                # 组合式函数（usePermission、useDict 等）
│   ├── directives/                 # 自定义指令（按钮权限等）
│   ├── layouts/                    # 布局（侧边栏 / 顶栏 / 标签页）
│   ├── utils/                      # 工具函数（axios 封装等）
│   ├── styles/                     # 全局样式
│   └── __tests__/ 及各模块 __tests__/  # 单元测试（Vitest）
└── playwright.config.ts / vitest.config.ts / tsconfig*.json
```

## 约定

- 所有接口标识符为 **GUID 字符串**（36 位），前端一律用 `string` 表示，禁止 `Number(id)`
- 审计字段统一为 `createdAt / createdBy / updatedAt / updatedBy`，备注统一为 `description`
- 树形数据根节点的父 Id 为全零 GUID `'00000000-0000-0000-0000-000000000000'`
- Element Plus 组件与 vue/vue-router/pinia API 均为自动导入（`auto-imports.d.ts` / `components.d.ts`）

## 环境要求

- Node.js >= 26.0.0
- pnpm（包管理器）
- 可访问的后端服务（默认代理到 `http://localhost:5000`，见 `canexus-api-netcore/README.md`）

## 本地运行

```bash
pnpm install          # 安装依赖
pnpm dev              # 启动开发服务器（/api 请求代理到 VITE_PROXY_TARGET）
pnpm type-check       # 类型检查（vue-tsc）
pnpm lint             # oxlint 代码检查
pnpm format           # oxfmt 格式化
pnpm test:unit        # Vitest 单元测试
pnpm test:e2e         # Playwright E2E 测试
pnpm build            # 生产构建（并行执行 type-check + vite build）
pnpm preview          # 本地预览构建产物
```

> IDE 提示：项目使用 `typescript-native-bridge`（tsgo）作为工作区 TypeScript，安装依赖后在 VS Code 中选择「使用工作区版本」即可。

## 工程化规范（Git Hooks + CI）

### Git Hooks（husky + lint-staged，仓库根级）

> 钩子配置在**仓库根目录**（`.husky/` 与 `.git` 同级），根目录 `package.json` 持有 husky / lint-staged，覆盖前端与后端。克隆后先在仓库根执行 `pnpm install`（`prepare` 脚本自动配置 `core.hooksPath`），再进本目录 `pnpm install` 安装前端依赖。

| 钩子 | 时机 | 执行内容 |
| --- | --- | --- |
| `pre-commit` | `git commit` 前 | lint-staged（配置于根 `.lintstagedrc.mjs`）对暂存文件分类检查：前端文件（`*.{js,mjs,cjs,ts,vue}`）经 `pnpm -C canexus-admin-vue` 以本目录为 cwd 执行 `oxlint --fix` + `oxfmt`；后端 `.cs` 文件执行 `dotnet format --include`（路径转换为仓库相对路径后传入）。自动修复后重新暂存，无法修复的问题则提交失败 |
| `pre-push` | `git push` 前 | 前端全量 `pnpm type-check`（vue-tsc）+ `pnpm test:unit run`（Vitest）+ 后端 `dotnet build` 编译检查；任一失败则推送被拒绝 |

配置位置：

- 仓库根 `package.json`（husky / lint-staged 依赖与 prepare 脚本）
- 仓库根 `.lintstagedrc.mjs`（匹配规则与执行命令；`.cs` 用函数配置把 lint-staged 传入的绝对路径转为仓库相对路径——`dotnet format --include` 不识别绝对路径，会静默跳过）
- 仓库根 `.husky/pre-commit`、`.husky/pre-push`（钩子脚本）

如需临时跳过钩子（不推荐）：`git commit --no-verify` / `git push --no-verify`。

## 部署说明

1. **构建**：`pnpm build`，产物输出至 `dist/`（纯静态文件）
2. **接口地址**：生产环境请求基础路径由 `.env.production` 的 `VITE_API_BASE_URL=/api` 决定，需由网关 / nginx 将 `/api` 反向代理到后端服务
3. **nginx 参考配置**：

```nginx
server {
    listen 80;
    root /var/www/canexus-admin-vue/dist;
    index index.html;

    # 安全响应头（CSP 按需放宽，如头像允许外链域名时在 img-src 追加）
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'none'" always;

    location / {
        try_files $uri $uri/ /index.html;   # history 路由回退
    }

    location /api {
        proxy_pass http://127.0.0.1:5000;   # 后端 canexus-api-netcore
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

4. **多租户**：后端开启多租户后，前端登录请求可携带 `X-Tenant-Id` 请求头（GUID 格式）
