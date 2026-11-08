/**
 * 路由配置。
 *
 * 包含静态常量路由（登录页、后台布局首页、403、404 通配）与守卫注册；
 * 动态路由由守卫内按后端菜单数据注册到布局路由下，
 * 路由 meta 约定：title / icon / keepAlive / hidden / order / affix / perms。
 */
import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import { CHANGE_PASSWORD_ROUTE_PATH, DASHBOARD_ROUTE_PATH, LAYOUT_ROUTE_NAME } from './constants'
import { setupRouterGuard } from './guards'

// 常量统一收敛至 ./constants（重新导出保持既有导入路径兼容）
export {
  CHANGE_PASSWORD_ROUTE_PATH,
  DASHBOARD_ROUTE_PATH,
  LAYOUT_ROUTE_NAME,
  LOGIN_ROUTE_PATH,
} from './constants'

/** 路由元信息约定 */
declare module 'vue-router' {
  interface RouteMeta {
    /** 标题（菜单 / 标签页 / 面包屑显示） */
    title?: string
    /** 图标名称（AppIcon 图标名） */
    icon?: string
    /** 是否缓存页面（配合 keep-alive，需组件 name 与路由 name 一致） */
    keepAlive?: boolean
    /** 是否在菜单中隐藏 */
    hidden?: boolean
    /** 排序权重（越小越靠前，动态路由排序使用） */
    order?: number
    /** 是否固定标签（标签页不可关闭，如首页） */
    affix?: boolean
    /** 路由级权限标识（任一匹配即可访问，动态路由由后端下发） */
    perms?: string[]
  }
}

/** 静态常量路由 */
export const constantRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/login/index.vue'),
    meta: { title: '登录', hidden: true },
  },
  {
    // 自助改密页：须已登录才能访问（守卫校验令牌），待改密用户仅可停留此页
    path: CHANGE_PASSWORD_ROUTE_PATH,
    name: 'ChangePassword',
    component: () => import('@/views/login/ChangePassword.vue'),
    meta: { title: '修改密码', hidden: true },
  },
  {
    path: '/',
    name: LAYOUT_ROUTE_NAME,
    component: () => import('@/layouts/AdminLayout.vue'),
    redirect: DASHBOARD_ROUTE_PATH,
    meta: { title: '首页', icon: 'HomeFilled' },
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/dashboard/index.vue'),
        meta: { title: '仪表盘', icon: 'Odometer', keepAlive: true, affix: true, order: 1 },
      },
      {
        // 字典数据维护页：从字典类型列表行内跳转进入，不随菜单下发（常量路由），
        // path 与动态菜单下发的 /system/dict 不冲突
        path: 'system/dict/data',
        name: 'DictData',
        component: () => import('@/views/system/dict/data.vue'),
        meta: { title: '字典数据', hidden: true, perms: ['system:dict:query'] },
      },
    ],
  },
  {
    path: '/403',
    name: 'Forbidden',
    component: () => import('@/views/error/403.vue'),
    meta: { title: '403', hidden: true },
  },
  {
    // 404 通配兜底：vue-router 按路由评分匹配，动态路由注册后仍优先于通配命中
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: () => import('@/views/error/404.vue'),
    meta: { title: '404', hidden: true },
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: constantRoutes,
})

/** chunk 加载失败整页刷新标记（sessionStorage，按目标路径记录防止刷新循环） */
const CHUNK_RELOAD_FLAG = 'super_admin_chunk_reload'

// 懒加载 chunk 失败兜底：发版后旧 chunk 失效（Failed to fetch dynamically imported module），
// 对同一目标路径最多自动整页刷新一次，刷新成功后的首次导航会清除标记
router.onError((error, to) => {
  const isChunkError =
    /Failed to fetch dynamically imported module/i.test(error.message) ||
    /Importing a module script failed/i.test(error.message)
  if (isChunkError && sessionStorage.getItem(CHUNK_RELOAD_FLAG) !== to.fullPath) {
    sessionStorage.setItem(CHUNK_RELOAD_FLAG, to.fullPath)
    window.location.replace(to.fullPath)
  }
})

// 导航成功后清除刷新标记，保留后续发版的自动恢复能力
router.afterEach(() => {
  sessionStorage.removeItem(CHUNK_RELOAD_FLAG)
})

// 注册全局守卫（登录拦截 + 动态路由加载 + 路由级权限校验）
setupRouterGuard(router)

export default router
