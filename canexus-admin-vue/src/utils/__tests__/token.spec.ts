import { beforeEach, describe, expect, it } from 'vitest'
import {
  ACCESS_TOKEN_KEY,
  MUST_CHANGE_PASSWORD_KEY,
  REFRESH_TOKEN_KEY,
  clearTokens,
  getAccessToken,
  getMustChangePassword,
  getRefreshToken,
  setAccessToken,
  setMustChangePassword,
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

  it('强制改密标记未设置时返回 false，写入 true 后可读取', () => {
    expect(getMustChangePassword()).toBe(false)
    setMustChangePassword(true)
    expect(getMustChangePassword()).toBe(true)
    expect(localStorage.getItem(MUST_CHANGE_PASSWORD_KEY)).toBe('true')
    setMustChangePassword(false)
    expect(getMustChangePassword()).toBe(false)
    expect(localStorage.getItem(MUST_CHANGE_PASSWORD_KEY)).toBeNull()
  })

  it('clearTokens 清空全部令牌与强制改密标记', () => {
    setAccessToken('access-1')
    setRefreshToken('refresh-1')
    setMustChangePassword(true)
    clearTokens()
    expect(getAccessToken()).toBe('')
    expect(getRefreshToken()).toBe('')
    expect(getMustChangePassword()).toBe(false)
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull()
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
    expect(localStorage.getItem(MUST_CHANGE_PASSWORD_KEY)).toBeNull()
  })
})
