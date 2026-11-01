/**
 * axios 请求封装。
 *
 * - baseURL 取环境变量 VITE_API_BASE_URL（默认 /api）
 * - 请求拦截器自动附带 `Authorization: Bearer <accessToken>`
 * - 响应拦截器解包统一响应 Result<T>：code === 200 直接返回 data
 * - 401 时使用 refreshToken 静默刷新一次并重放请求（并发请求共享刷新、排队重放、防循环）
 * - 刷新失败清除令牌并跳转登录页
 * - 业务失败与网络异常统一通过 ElMessage 提示
 */
import axios from 'axios'
import type {
  AxiosError,
  AxiosInstance,
  AxiosRequestConfig,
  InternalAxiosRequestConfig,
} from 'axios'
import { ElMessage } from 'element-plus'
import type { Result } from '@/api/types'
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setAccessToken,
  setRefreshToken,
} from './token'

/** 业务成功状态码 */
const SUCCESS_CODE = 200

/** 登录状态失效码 */
const UNAUTHORIZED_CODE = 401

/** 刷新令牌接口地址（认证模块接入后沿用该契约） */
const REFRESH_URL = '/auth/refresh'

/** 刷新令牌返回的凭证结构 */
interface AuthTokens {
  accessToken: string
  refreshToken: string
}

/** axios 实例（文件下载等特殊场景可直接使用） */
const instance: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  timeout: 15_000,
})

/** 展示错误提示（grouping 合并连续相同提示，避免并发请求重复弹窗） */
function showErrorMessage(message: string): void {
  ElMessage.error({ message, grouping: true })
}

/** 跳转登录页（已处于登录页时不再跳转，防止循环） */
function redirectToLogin(): void {
  if (window.location.pathname !== '/login') {
    window.location.href = '/login'
  }
}

/** 登录状态失效：清除令牌并跳转登录页 */
function handleAuthExpired(): void {
  clearTokens()
  redirectToLogin()
}

/** 使用 refreshToken 换发新令牌；成功返回 true */
async function refreshTokens(): Promise<boolean> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) {
    return false
  }
  try {
    // 使用独立的 axios 调用，不经过本实例拦截器，避免循环刷新
    const response = await axios.post<Result<AuthTokens>>(
      REFRESH_URL,
      { refreshToken },
      { baseURL: instance.defaults.baseURL },
    )
    const { code, data } = response.data
    if (code === SUCCESS_CODE && data) {
      setAccessToken(data.accessToken)
      setRefreshToken(data.refreshToken)
      return true
    }
    return false
  } catch {
    return false
  }
}

/** 正在进行的刷新任务（并发 401 共享同一次刷新，完成后置空） */
let refreshing: Promise<boolean> | null = null

/** 处理 401：静默刷新令牌并重放原请求 */
function handleUnauthorized(config: InternalAxiosRequestConfig): Promise<unknown> {
  // 刷新接口自身的 401：说明 refreshToken 已失效，直接退出，防止循环刷新
  if (config.url?.includes(REFRESH_URL)) {
    handleAuthExpired()
    showErrorMessage('登录状态已失效，请重新登录')
    return Promise.reject(new Error('未授权'))
  }

  // 并发请求共享同一次刷新（天然排队），刷新完成后各自重放
  refreshing ??= refreshTokens().finally(() => {
    refreshing = null
  })

  return refreshing.then((success) => {
    if (!success) {
      handleAuthExpired()
      showErrorMessage('登录状态已失效，请重新登录')
      return Promise.reject(new Error('未授权'))
    }
    // 刷新成功：使用原配置重放请求（请求拦截器会重新注入新令牌）
    return instance.request(config)
  })
}

/** HTTP 状态码错误文案 */
function getHttpErrorMessage(status: number): string {
  const messages: Record<number, string> = {
    400: '请求参数错误',
    401: '登录状态已失效，请重新登录',
    403: '没有操作权限',
    404: '请求的资源不存在',
    500: '服务器内部错误',
    502: '网关错误',
    503: '服务不可用',
    504: '网关超时',
  }
  return messages[status] ?? `请求失败（${status}）`
}

/** 统一处理 HTTP 层错误（网络异常 / 超时 / 非 2xx 状态码） */
function handleHttpError(error: AxiosError): Promise<unknown> {
  const { response } = error
  if (response) {
    if (response.status === UNAUTHORIZED_CODE) {
      return handleUnauthorized(response.config)
    }
    // 优先展示后端统一响应中的 message
    const resultMessage = (response.data as Partial<Result> | undefined)?.message
    showErrorMessage(resultMessage || getHttpErrorMessage(response.status))
    return Promise.reject(error)
  }
  const message =
    error.code === 'ECONNABORTED' ? '请求超时，请稍后重试' : '网络异常，请检查网络连接'
  showErrorMessage(message)
  return Promise.reject(error)
}

// 请求拦截器：注入访问令牌
instance.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：解包 Result 并统一处理错误
instance.interceptors.response.use(
  (response) => {
    const result = response.data as Result | undefined
    // 非统一包裹结构（如二进制流）原样返回
    if (!result || typeof result !== 'object' || typeof result.code !== 'number') {
      return response.data
    }
    if (result.code === SUCCESS_CODE) {
      return result.data
    }
    if (result.code === UNAUTHORIZED_CODE) {
      return handleUnauthorized(response.config)
    }
    // 业务失败：统一提示并拒绝
    showErrorMessage(result.message || '请求失败')
    return Promise.reject(new Error(result.message || '请求失败'))
  },
  (error: AxiosError) => handleHttpError(error),
)

/**
 * 类型化请求方法：返回解包后的业务数据（T）。
 *
 * 响应拦截器已将统一响应解包为 data，故此处对 axios 返回值做类型断言。
 */
const request = {
  get<T = unknown>(url: string, config?: AxiosRequestConfig): Promise<T> {
    return instance.get(url, config) as Promise<T>
  },
  post<T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig): Promise<T> {
    return instance.post(url, data, config) as Promise<T>
  },
  put<T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig): Promise<T> {
    return instance.put(url, data, config) as Promise<T>
  },
  delete<T = unknown>(url: string, config?: AxiosRequestConfig): Promise<T> {
    return instance.delete(url, config) as Promise<T>
  },
}

export default request
export { instance as axiosInstance }
