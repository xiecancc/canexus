/**
 * 路由守卫：登录拦截与动态路由加载。
 *
 * 流程：
 * - 白名单（/login）直接放行
 * - 无令牌 → /login?redirect=完整路径（改密页不携带回跳，避免改密后回到受限页）
 * - 待改密用户（后端 40302）→ 仅允许停留在 /change-password，其余页面一律收敛到改密页
 * - 已登录且动态路由未加载 → 拉取用户信息与路由表，addRoute 注册后以 replace 重新进入
 * - 路由级权限：meta.perms 存在且用户无对应权限 → 403 页
 */
import type { Router } from 'vue-router'
import { CHANGE_PASSWORD_ROUTE_PATH, LOGIN_ROUTE_PATH } from './constants'
import { usePermission } from '@/composables/usePermission'
import { useAuthStore } from '@/stores/auth'
import { getAccessToken, getMustChangePassword } from '@/utils/token'

/** 免登录白名单 */
const WHITE_LIST = [LOGIN_ROUTE_PATH]

/** 注册全局路由守卫（在 router/index.ts 中调用） */
export function setupRouterGuard(router: Router): void {
  router.beforeEach(async (to) => {
    // 白名单直接放行
    if (WHITE_LIST.includes(to.path)) {
      return true
    }

    const isChangePasswordPage = to.path === CHANGE_PASSWORD_ROUTE_PATH

    // 无令牌：携带回跳地址前往登录页；改密页须已登录，不保留回跳避免改密后落回受限页
    if (!getAccessToken()) {
      return isChangePasswordPage
        ? { path: LOGIN_ROUTE_PATH, replace: true }
        : { path: LOGIN_ROUTE_PATH, query: { redirect: to.fullPath }, replace: true }
    }

    // 待改密用户：后端 MustChangePasswordMiddleware 会以 40302 拦截所有受保护端点，
    // 必须在拉取用户信息 / 动态路由之前收敛，避免连锁失败
    if (getMustChangePassword()) {
      return isChangePasswordPage ? true : { path: CHANGE_PASSWORD_ROUTE_PATH, replace: true }
    }

    // 主动改密：改密页是独立静态路由，不依赖后端菜单与路由表
    if (isChangePasswordPage) {
      return true
    }

    // 已登录访问登录页：回首页
    if (to.path === LOGIN_ROUTE_PATH) {
      return { path: '/', replace: true }
    }

    const authStore = useAuthStore()

    // 动态路由未加载：先拉取用户信息与路由表，注册后重新进入目标路由
    if (!authStore.routesLoaded) {
      try {
        await authStore.fetchUserInfo()
        await authStore.loadRoutes()
        // 注意：不能展开 to（其中含已解析的 name，如 NotFound 会按名称再次命中），
        // 仅携带路径信息触发按 path 重新解析，确保新注册的动态路由参与匹配
        return { path: to.path, query: to.query, hash: to.hash, replace: true }
      } catch {
        // 用户信息或路由表拉取失败：清理认证状态回登录页
        authStore.resetAuth()
        return { path: '/login', query: { redirect: to.fullPath }, replace: true }
      }
    }

    // 路由级权限校验：meta.perms 任一匹配即通过（超管通配恒真）
    const perms = to.meta.perms
    if (perms?.length) {
      const { hasPerm } = usePermission()
      if (!hasPerm(perms)) {
        return { path: '/403', replace: true }
      }
    }

    return true
  })
}
