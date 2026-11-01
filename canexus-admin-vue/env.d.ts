/// <reference types="vite/client" />

// 自定义环境变量类型（与 .env.* 文件保持一致）
interface ImportMetaEnv {
  /** 接口请求基础路径 */
  readonly VITE_API_BASE_URL: string
  /** 开发环境接口代理目标（后端服务地址） */
  readonly VITE_PROXY_TARGET: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
