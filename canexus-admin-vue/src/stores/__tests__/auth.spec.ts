import { beforeEach, describe, expect, it, vi } from 'vitest'

// mock 请求封装：认证 store 的接口调用均经由 @/api/auth → @/utils/request
vi.mock('@/utils/request', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

import { createPinia, setActivePinia } from 'pinia'
import router from '@/router'
import { ACCESS_TOKEN_KEY, getMustChangePassword, REFRESH_TOKEN_KEY } from '@/utils/token'
import type { LoginResponse, RouteVO, UserInfo } from '@/api/auth'
import request from '@/utils/request'
import { useAuthStore } from '../auth'

/** 将 mock 后的 request 断言为可编程对象 */
const mockedRequest = request as unknown as {
  get: ReturnType<typeof vi.fn>
  post: ReturnType<typeof vi.fn>
}

/** 登录接口 mock 响应 */
const loginResponse: LoginResponse = {
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  // 后端 Jwt:AccessTokenMinutes 当前默认 30 分钟
  expiresIn: 1800,
  mustChangePassword: false,
}

/** 用户信息 mock 响应（超管） */
const userInfo: UserInfo = {
  userId: '00000000-0000-0000-0000-000000000001',
  userName: 'admin',
  nickName: '超级管理员',
  avatar: '',
  roles: ['admin'],
  permissions: ['*:*:*'],
}

/** 动态路由 mock 响应（含按钮类型与未知组件） */
const routeVOs: RouteVO[] = [
  {
    name: 'System',
    path: '/system',
    component: 'Layout',
    redirect: '/system/user',
    meta: { title: '系统管理', icon: 'Setting', order: 2 },
    children: [
      { name: 'User', path: 'user', component: 'system/user', meta: { title: '用户管理' } },
      // 按钮类型：无 component 且无子级，不应生成路由
      { path: 'user-btn', meta: { title: '新增用户按钮', hidden: true } },
    ],
  },
]

describe('useAuthStore 认证状态', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('login 调用登录接口并将令牌写入 store 与 localStorage', async () => {
    mockedRequest.post.mockResolvedValueOnce(loginResponse)
    const store = useAuthStore()

    await store.login({
      userName: 'admin',
      password: '123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    })

    // 请求路径与参数符合后端契约
    expect(mockedRequest.post).toHaveBeenCalledWith('/auth/login', {
      userName: 'admin',
      password: '123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    })
    expect(store.accessToken).toBe('access-1')
    expect(store.refreshToken).toBe('refresh-1')
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe('access-1')
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1')
  })

  it('login 响应要求强制改密时写入标记，resetAuth 后清除', async () => {
    mockedRequest.post.mockResolvedValueOnce({ ...loginResponse, mustChangePassword: true })
    const store = useAuthStore()

    await store.login({
      userName: 'admin',
      password: '123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    })

    expect(store.mustChangePassword).toBe(true)
    expect(getMustChangePassword()).toBe(true)

    store.resetAuth()
    expect(store.mustChangePassword).toBe(false)
    expect(getMustChangePassword()).toBe(false)
  })

  it('fetchUserInfo 保存用户信息并派生 roles/permissions', async () => {
    mockedRequest.get.mockResolvedValueOnce(userInfo)
    const store = useAuthStore()

    await store.fetchUserInfo()

    expect(mockedRequest.get).toHaveBeenCalledWith('/auth/user-info')
    expect(store.userInfo?.userName).toBe('admin')
    expect(store.roles).toEqual(['admin'])
    expect(store.permissions).toEqual(['*:*:*'])
  })

  it('loadRoutes 拉取路由表并注册到布局路由下（按钮类型不生成路由）', async () => {
    mockedRequest.get.mockResolvedValueOnce(routeVOs)
    const store = useAuthStore()
    expect(router.hasRoute('System')).toBe(false)

    await store.loadRoutes()

    expect(mockedRequest.get).toHaveBeenCalledWith('/auth/routes')
    expect(store.routesLoaded).toBe(true)
    expect(store.dynamicRoutes).toHaveLength(1)
    // 动态路由已注册（顶级挂载于 AdminLayout 下）
    expect(router.hasRoute('System')).toBe(true)
    expect(router.hasRoute('User')).toBe(true)
    // 按钮类型被过滤
    expect(router.hasRoute('UserBtn')).toBe(false)

    // 重置后动态路由被移除
    store.resetAuth()
    expect(router.hasRoute('System')).toBe(false)
    expect(router.hasRoute('User')).toBe(false)
    expect(store.dynamicRoutes).toHaveLength(0)
    expect(store.routesLoaded).toBe(false)
  })

  it('loadRoutes 对映射不到的组件回落占位页并告警', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    mockedRequest.get.mockResolvedValueOnce([
      // name 缺失：按 path 生成路由名
      { path: '/demo', component: 'login', meta: { title: '演示' } },
      // 组件不存在：回落建设中占位页
      { path: '/missing', component: 'no/such/module', meta: { title: '缺失' } },
    ] satisfies RouteVO[])
    const store = useAuthStore()

    await store.loadRoutes()

    expect(router.hasRoute('Demo')).toBe(true)
    expect(warnSpy).toHaveBeenCalled()
    store.resetAuth()
    warnSpy.mockRestore()
  })

  it('logout 调用登出接口并清空令牌与状态', async () => {
    mockedRequest.post.mockResolvedValueOnce(loginResponse)
    mockedRequest.get.mockResolvedValueOnce(userInfo)
    mockedRequest.post.mockResolvedValueOnce(null)
    const store = useAuthStore()

    await store.login({
      userName: 'admin',
      password: '123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    })
    await store.fetchUserInfo()
    await store.logout()

    expect(mockedRequest.post).toHaveBeenCalledWith('/auth/logout')
    expect(store.accessToken).toBe('')
    expect(store.refreshToken).toBe('')
    expect(store.userInfo).toBeNull()
    expect(store.routesLoaded).toBe(false)
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull()
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
  })

  it('logout 接口异常时同样清空本地状态', async () => {
    mockedRequest.post.mockResolvedValueOnce(loginResponse)
    const store = useAuthStore()
    await store.login({
      userName: 'admin',
      password: '123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    })

    mockedRequest.post.mockRejectedValueOnce(new Error('网络异常'))
    await expect(store.logout()).rejects.toThrow('网络异常')

    expect(store.accessToken).toBe('')
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull()
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
  })
})
