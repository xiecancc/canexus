# admin-vue — 管理端前端

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
| 系统管理 | `views/system` | 用户 / 角色 / 菜单 / 部门（树形）/ 字典 / 参数配置 / 通知公告 / 租户 / 定时任务（含执行日志抽屉） |
| 系统监控 | `views/monitor` | 操作日志、登录日志 |
| 错误页 | `views/error` | 403 / 404 等 |
| 通用能力 | `components` `composables` `directives` | 字典标签、权限指令、布局、动态路由等 |

## 目录结构

```
admin-vue/
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
- 可访问的后端服务（默认代理到 `http://localhost:5000`，见 `api-netcore/README.md`）

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

## 部署说明

1. **构建**：`pnpm build`，产物输出至 `dist/`（纯静态文件）
2. **接口地址**：生产环境请求基础路径由 `.env.production` 的 `VITE_API_BASE_URL=/api` 决定，需由网关 / nginx 将 `/api` 反向代理到后端服务
3. **nginx 参考配置**：

```nginx
server {
    listen 80;
    root /var/www/admin-vue/dist;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;   # history 路由回退
    }

    location /api {
        proxy_pass http://127.0.0.1:5000;   # 后端 api-netcore
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

4. **多租户**：后端开启多租户后，前端登录请求可携带 `X-Tenant-Id` 请求头（GUID 格式）
