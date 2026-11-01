import { beforeEach, describe, expect, it } from 'vitest'
import {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setAccessToken,
  setRefreshToken,
} from '../token'

describe('token 存取工具', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('key 统一使用 super_admin_ 前缀', () => {
    expect(ACCESS_TOKEN_KEY).toBe('super_admin_access_token')
    expect(REFRESH_TOKEN_KEY).toBe('super_admin_refresh_token')
  })

  it('写入后可读取 accessToken 与 refreshToken', () => {
    setAccessToken('access-1')
    setRefreshToken('refresh-1')
    expect(getAccessToken()).toBe('access-1')
    expect(getRefreshToken()).toBe('refresh-1')
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe('access-1')
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1')
  })

  it('未设置时返回空字符串', () => {
    expect(getAccessToken()).toBe('')
    expect(getRefreshToken()).toBe('')
  })

  it('clearTokens 清空全部令牌', () => {
    setAccessToken('access-1')
    setRefreshToken('refresh-1')
    clearTokens()
    expect(getAccessToken()).toBe('')
    expect(getRefreshToken()).toBe('')
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull()
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
  })
})
