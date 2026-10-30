/**
 * 操作日志模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import { cleanOperLogApi, deleteOperLogApi, getOperLogPageApi } from '../operlog'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

describe('操作日志模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /system/oper-log/page 且查询条件（含日期范围）透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = {
      title: '用户管理',
      businessType: '2',
      status: 1,
      operUserName: 'admin',
      beginTime: '2026-01-01',
      endTime: '2026-01-31',
      pageIndex: 1,
      pageSize: 10,
    }
    await expect(getOperLogPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/oper-log/page', { params })
  })

  it('删除单条：DELETE /system/oper-log/{id}', async () => {
    await deleteOperLogApi('00000000-0000-0000-0000-000000000009')
    expect(mockedDelete).toHaveBeenCalledWith('/system/oper-log/00000000-0000-0000-0000-000000000009')
  })

  it('清空全部：POST /system/oper-log/clean', async () => {
    await cleanOperLogApi()
    expect(mockedPost).toHaveBeenCalledWith('/system/oper-log/clean')
  })
})
