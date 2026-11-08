/**
 * 请求封装测试：验证后端强制改密拦截码 40302 的前端处理。
 *
 * 通过注入 axios adapter 模拟响应，并预先把 pathname 置为改密页，
 * 使拦截分支只做「写标记 + 提示 + 拒绝」，避免 jsdom 未实现的整页导航。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('element-plus', () => ({
  ElMessage: {
    error: vi.fn(),
    success: vi.fn(),
    warning: vi.fn(),
  },
}))

import { ElMessage } from 'element-plus'
import { axiosInstance } from '../request'
import { getMustChangePassword } from '../token'

const originalAdapter = axiosInstance.defaults.adapter

describe('request 强制改密拦截（40302）', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    window.history.pushState({}, '', '/change-password')
  })

  afterEach(() => {
    axiosInstance.defaults.adapter = originalAdapter
    window.history.pushState({}, '', '/')
  })

  it('HTTP 403 + 业务码 40302：写入标记、提示并拒绝请求', async () => {
    axiosInstance.defaults.adapter = async () => {
      const error = new Error('Forbidden') as Error & {
        response: { status: number; data: unknown; config: unknown }
      }
      error.response = {
        status: 403,
        data: { code: 40302, message: '首次登录须先修改初始密码', data: null },
        config: {},
      }
      throw error
    }

    await expect(axiosInstance.get('/system/user/page')).rejects.toThrow()
    expect(getMustChangePassword()).toBe(true)
    expect(ElMessage.error).toHaveBeenCalledWith(
      expect.objectContaining({ message: expect.stringContaining('初始密码') }),
    )
  })

  it('HTTP 200 + 业务码 40302：同样写入标记并拒绝', async () => {
    axiosInstance.defaults.adapter = async () =>
      ({
        status: 200,
        statusText: 'OK',
        headers: {},
        config: {},
        data: { code: 40302, message: '首次登录须先修改初始密码', data: null },
      }) as never

    await expect(axiosInstance.get('/system/user/page')).rejects.toThrow()
    expect(getMustChangePassword()).toBe(true)
  })

  it('普通业务失败：不写入强制改密标记', async () => {
    axiosInstance.defaults.adapter = async () =>
      ({
        status: 200,
        statusText: 'OK',
        headers: {},
        config: {},
        data: { code: 40000, message: '参数错误', data: null },
      }) as never

    await expect(axiosInstance.get('/system/user/page')).rejects.toThrow()
    expect(getMustChangePassword()).toBe(false)
  })

  it('写命令 data=false（恢复幂等 no-op）仍是成功响应，原样解包不报错', async () => {
    axiosInstance.defaults.adapter = async () =>
      ({
        status: 200,
        statusText: 'OK',
        headers: {},
        config: {},
        data: { code: 200, message: '参数不存在或未删除，无需恢复', data: false },
      }) as never

    await expect(axiosInstance.patch('/system/config/1/1/restore')).resolves.toBe(false)
    expect(ElMessage.error).not.toHaveBeenCalled()
  })
})
