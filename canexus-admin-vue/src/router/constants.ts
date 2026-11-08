/**
 * 路由常量（独立模块，避免 index.ts ↔ guards.ts 循环引用导致顶层常量 TDZ 崩溃）。
 */

/** 后台布局路由名称（动态路由将以此为父级挂载） */
export const LAYOUT_ROUTE_NAME = 'AdminLayout'

/** 首页路径 */
export const DASHBOARD_ROUTE_PATH = '/dashboard'

/** 登录页路径 */
export const LOGIN_ROUTE_PATH = '/login'

/** 自助修改密码页路径（首次登录强制改密与用户主动改密共用） */
export const CHANGE_PASSWORD_ROUTE_PATH = '/change-password'
