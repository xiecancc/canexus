/**
 * 实体变更日志模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import { getEntityChangeLogPageApi } from '../entitychange'

const mockedGet = request.get as unknown as Mock

describe('实体变更日志模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /system/entity-change/page 且查询条件透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = {
      entityName: 'SysUser',
      entityId: '00000000-0000-0000-0000-000000000001',
      fieldName: 'status',
      pageIndex: 1,
      pageSize: 10,
    }
    await expect(getEntityChangeLogPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/entity-change/page', { params })
  })

  it('空筛选条件仅透传分页参数', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    await getEntityChangeLogPageApi({ pageIndex: 1, pageSize: 10 })
    expect(mockedGet).toHaveBeenCalledWith('/system/entity-change/page', {
      params: { pageIndex: 1, pageSize: 10 },
    })
  })
})
