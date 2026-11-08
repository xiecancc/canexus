/**
 * Token 存取工具。
 *
 * localStorage key 统一使用 super_admin_ 前缀，避免与其他应用冲突。
 */

/** 存储键统一前缀 */
const STORAGE_PREFIX = 'super_admin_'

/** accessToken 存储键 */
export const ACCESS_TOKEN_KEY = `${STORAGE_PREFIX}access_token`

/** refreshToken 存储键 */
export const REFRESH_TOKEN_KEY = `${STORAGE_PREFIX}refresh_token`

/** 强制改密标记存储键（登录 / 刷新响应及 40302 拦截时写入） */
export const MUST_CHANGE_PASSWORD_KEY = `${STORAGE_PREFIX}must_change_password`

/** 读取 accessToken（未设置时返回空字符串） */
export function getAccessToken(): string {
  return localStorage.getItem(ACCESS_TOKEN_KEY) ?? ''
}

/** 写入 accessToken */
export function setAccessToken(token: string): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, token)
}

/** 读取 refreshToken（未设置时返回空字符串） */
export function getRefreshToken(): string {
  return localStorage.getItem(REFRESH_TOKEN_KEY) ?? ''
}

/** 写入 refreshToken */
export function setRefreshToken(token: string): void {
  localStorage.setItem(REFRESH_TOKEN_KEY, token)
}

/** 读取强制改密标记（未设置时返回 false） */
export function getMustChangePassword(): boolean {
  return localStorage.getItem(MUST_CHANGE_PASSWORD_KEY) === 'true'
}

/** 写入强制改密标记；false 时直接移除键，避免残留状态阻止后续导航 */
export function setMustChangePassword(required: boolean): void {
  if (required) {
    localStorage.setItem(MUST_CHANGE_PASSWORD_KEY, 'true')
  } else {
    localStorage.removeItem(MUST_CHANGE_PASSWORD_KEY)
  }
}

/** 清除全部令牌与强制改密标记 */
export function clearTokens(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
  localStorage.removeItem(MUST_CHANGE_PASSWORD_KEY)
}
