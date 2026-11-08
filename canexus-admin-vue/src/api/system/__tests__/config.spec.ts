/**
 * 参数配置模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
  createConfigApi,
  deleteConfigApi,
  getConfigByKeyApi,
  getConfigPageApi,
  updateConfigApi,
} from '../config'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用参数 ID（GUID 字符串） */
const CONFIG_ID = '00000000-0000-0000-0000-000000000005'

/** 测试用乐观锁版本号（GUID 字符串，取自列表响应的 version 字段） */
const VERSION = '00000000-0000-0000-0000-00000000000a'

describe('参数配置模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询参数：GET /system/config/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { keyword: '皮肤', pageIndex: 3, pageSize: 20 }
    await expect(getConfigPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/config/page', { params })
  })

  it('按键名查询参数值：GET /system/config/key/{configKey} 且 data 为字符串', async () => {
    mockedGet.mockResolvedValueOnce('dark')
    await expect(getConfigByKeyApi('sys.index.skinName')).resolves.toBe('dark')
    expect(mockedGet).toHaveBeenCalledWith('/system/config/key/sys.index.skinName')
  })

  it('创建参数：POST /system/config 且请求体透传', async () => {
    const body = { configName: '主框架皮肤', configKey: 'sys.index.skinName', configValue: 'dark' }
    await createConfigApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/config', body)
  })

  it('更新参数：PUT /system/config/{id}/{version}', async () => {
    const body = { configName: '主框架皮肤', configKey: 'sys.index.skinName', configValue: 'light' }
    await updateConfigApi(CONFIG_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/config/${CONFIG_ID}/${VERSION}`, body)
  })

  it('删除参数：DELETE /system/config/{id}/{version}', async () => {
    await deleteConfigApi(CONFIG_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/config/${CONFIG_ID}/${VERSION}`)
  })
})
