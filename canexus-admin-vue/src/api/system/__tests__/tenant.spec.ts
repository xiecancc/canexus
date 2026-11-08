/**
 * 租户管理模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import {
  createTenantApi,
  deleteTenantApi,
  getTenantAllApi,
  getTenantPageApi,
  updateTenantApi,
} from '../tenant'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用租户 ID（GUID 字符串） */
const TENANT_ID = '00000000-0000-0000-0000-000000000008'

/** 测试用乐观锁版本号（GUID 字符串，取自列表响应的 version 字段） */
const VERSION = '00000000-0000-0000-0000-00000000000a'

describe('租户管理模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询租户：GET /system/tenant/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { keyword: '总公司', status: 1, pageIndex: 2, pageSize: 20 }
    await expect(getTenantPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/tenant/page', { params })
  })

  it('查询全部租户：GET /system/tenant/all 返回简要集合', async () => {
    const all = [
      {
        id: '00000000-0000-0000-0000-000000000001',
        tenantKey: 'super',
        tenantName: '总租户',
        status: 1,
      },
    ]
    mockedGet.mockResolvedValueOnce(all)
    await expect(getTenantAllApi()).resolves.toBe(all)
    expect(mockedGet).toHaveBeenCalledWith('/system/tenant/all')
  })

  it('创建租户：POST /system/tenant 且请求体透传', async () => {
    const body = { tenantKey: 'acme', tenantName: 'Acme 公司', status: 1 }
    await createTenantApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/tenant', body)
  })

  it('更新租户：PUT /system/tenant/{id}/{version}（标识随请求体提交但不可修改）', async () => {
    const body = { tenantKey: 'acme', tenantName: 'Acme 集团', status: 0 }
    await updateTenantApi(TENANT_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/tenant/${TENANT_ID}/${VERSION}`, body)
  })

  it('删除租户：DELETE /system/tenant/{id}/{version}', async () => {
    await deleteTenantApi(TENANT_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/tenant/${TENANT_ID}/${VERSION}`)
  })
})
