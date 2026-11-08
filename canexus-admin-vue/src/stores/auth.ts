/**
 * 认证状态：令牌、用户信息、动态路由。
 *
 * - 令牌读写统一走 utils/token.ts（localStorage，单一来源），store 内仅维护镜像状态
 * - 动态路由由后端 /auth/routes 下发，经 transformRoutes 转换后挂载到后台布局路由下
 * - 用户信息不持久化，刷新页面时由路由守卫重新拉取
 */
import { computed, ref } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import { defineStore } from 'pinia'
import type { LoginParams, LoginResponse, RouteVO, UserInfo } from '@/api/auth'
import { getRoutesApi, getUserInfoApi, loginApi, logoutApi } from '@/api/auth'
import router, { LAYOUT_ROUTE_NAME } from '@/router'
import { transformRoutes } from '@/router/dynamic'
import {
  clearTokens,
  getAccessToken,
  getMustChangePassword,
  getRefreshToken,
  setAccessToken,
  setMustChangePassword,
  setRefreshToken,
} from '@/utils/token'

/** 认证状态 */
export const useAuthStore = defineStore('auth', () => {
  /** 访问令牌（镜像，实际以 utils/token.ts 为准） */
  const accessToken = ref(getAccessToken())

  /** 刷新令牌（镜像，实际以 utils/token.ts 为准） */
  const refreshToken = ref(getRefreshToken())

  /** 是否须先修改初始密码（镜像；路由守卫以 utils/token.ts 为准，避免静默刷新后镜像滞后） */
  const mustChangePassword = ref(getMustChangePassword())

  /** 当前登录用户信息 */
  const userInfo = ref<UserInfo | null>(null)

  /** 动态路由是否已加载（守卫据此决定是否拉取路由表） */
  const routesLoaded = ref(false)

  /** 动态注册的顶级路由记录（供侧边栏菜单渲染） */
  const dynamicRoutes = ref<RouteRecordRaw[]>([])

  /** 角色标识集合 */
  const roles = computed(() => userInfo.value?.roles ?? [])

  /** 权限标识集合 */
  const permissions = computed(() => userInfo.value?.permissions ?? [])

  /** 用户昵称 */
  const nickName = computed(() => userInfo.value?.nickName ?? '')

  /** 用户头像 */
  const avatar = computed(() => userInfo.value?.avatar ?? '')

  /** 写入令牌（内存状态与 localStorage 同步） */
  function setTokens(access: string, refresh: string): void {
    accessToken.value = access
    refreshToken.value = refresh
    setAccessToken(access)
    setRefreshToken(refresh)
  }

  /** 账号密码登录：保存令牌与强制改密标记（用户信息与路由由守卫在跳转时拉取） */
  async function login(params: LoginParams): Promise<LoginResponse> {
    const data = await loginApi(params)
    setTokens(data.accessToken, data.refreshToken)
    setMustChangePassword(data.mustChangePassword === true)
    mustChangePassword.value = data.mustChangePassword === true
    return data
  }

  /** 拉取并保存当前用户信息 */
  async function fetchUserInfo(): Promise<UserInfo> {
    const data = await getUserInfoApi()
    userInfo.value = data
    return data
  }

  /** 拉取动态路由并注册到后台布局路由下（幂等由 routesLoaded 标记保证） */
  async function loadRoutes(): Promise<void> {
    const vos: RouteVO[] = await getRoutesApi()
    const records = transformRoutes(vos)
    // 顶级目录/菜单统一挂到现有 AdminLayout 的 children 下
    records.forEach((record) => router.addRoute(LAYOUT_ROUTE_NAME, record))
    dynamicRoutes.value = records
    routesLoaded.value = true
  }

  /** 清除认证状态：移除动态路由、清空用户信息与令牌 */
  function resetAuth(): void {
    dynamicRoutes.value.forEach((record) => {
      if (record.name && router.hasRoute(record.name)) {
        router.removeRoute(record.name)
      }
    })
    dynamicRoutes.value = []
    userInfo.value = null
    routesLoaded.value = false
    accessToken.value = ''
    refreshToken.value = ''
    mustChangePassword.value = false
    clearTokens()
  }

  /** 退出登录：调用登出接口后清理本地状态（接口失败也保证清理） */
  async function logout(): Promise<void> {
    try {
      await logoutApi()
    } finally {
      resetAuth()
    }
  }

  return {
    accessToken,
    refreshToken,
    mustChangePassword,
    userInfo,
    routesLoaded,
    dynamicRoutes,
    roles,
    permissions,
    nickName,
    avatar,
    setTokens,
    login,
    fetchUserInfo,
    loadRoutes,
    resetAuth,
    logout,
  }
})
