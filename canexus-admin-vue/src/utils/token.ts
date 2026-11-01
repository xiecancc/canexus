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

/** 清除全部令牌 */
export function clearTokens(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
}
