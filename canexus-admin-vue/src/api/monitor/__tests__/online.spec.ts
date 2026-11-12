/**
 * 在线会话模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import type { PagedResult } from '@/api/types'

vi.mock('@/utils/request', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

import request from '@/utils/request'
import { forceLogoutApi, getOnlineUserPageApi } from '../online'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock

/** 测试用会话记录 ID（GUID 字符串，取自列表行 id） */
const TOKEN_ID = '00000000-0000-0000-0000-000000000011'

describe('在线会话模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /monitor/online/page 且查询条件透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { userName: 'admin', ip: '127.0.0.1', pageIndex: 1, pageSize: 10 }
    await expect(getOnlineUserPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/monitor/online/page', { params })
  })

  it('强制下线：POST /monitor/online/{tokenId}/revoke（无请求体）', async () => {
    await forceLogoutApi(TOKEN_ID)
    expect(mockedPost).toHaveBeenCalledWith(`/monitor/online/${TOKEN_ID}/revoke`)
  })
})
