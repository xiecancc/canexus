/**
 * 认证模块接口测试：mock 请求层，验证登录响应契约与自助改密端点。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'

vi.mock('@/utils/request', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

import request from '@/utils/request'
import { changePasswordApi, loginApi } from '../auth'

const mockedPost = request.post as unknown as Mock

describe('认证模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('登录：POST /auth/login，响应携带 mustChangePassword 与 expiresIn（秒）', async () => {
    const response = {
      accessToken: 'access-1',
      refreshToken: 'refresh-1',
      expiresIn: 1800,
      mustChangePassword: true,
    }
    mockedPost.mockResolvedValueOnce(response)
    const params = {
      userName: 'admin',
      password: 'Aa123456',
      captchaId: 'captcha-1',
      captchaCode: 'abcd',
    }

    await expect(loginApi(params)).resolves.toBe(response)
    expect(mockedPost).toHaveBeenCalledWith('/auth/login', params)
  })

  it('自助改密：POST /auth/change-password 且请求体透传', async () => {
    mockedPost.mockResolvedValueOnce(null)
    const body = { oldPassword: 'Aa123456', newPassword: 'Bb654321' }

    await changePasswordApi(body)

    expect(mockedPost).toHaveBeenCalledWith('/auth/change-password', body)
  })
})
