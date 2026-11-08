/**
 * 字典模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
  createDictDataApi,
  createDictTypeApi,
  deleteDictDataApi,
  deleteDictTypeApi,
  getDictDataByTypeApi,
  getDictDataPageApi,
  getDictTypePageApi,
  updateDictDataApi,
  updateDictTypeApi,
} from '../dict'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用字典类型 ID（GUID 字符串） */
const DICT_TYPE_ID = '00000000-0000-0000-0000-000000000009'

/** 测试用字典数据 ID（GUID 字符串） */
const DICT_DATA_ID = '00000000-0000-0000-0000-000000000007'

/** 测试用乐观锁版本号（GUID 字符串，取自列表响应的 version 字段） */
const VERSION = '00000000-0000-0000-0000-00000000000a'

describe('字典模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询字典类型：GET /system/dict-type/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { keyword: '状态', pageIndex: 2, pageSize: 20 }
    await expect(getDictTypePageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/dict-type/page', { params })
  })

  it('创建字典类型：POST /system/dict-type 且请求体透传', async () => {
    const body = { dictName: '系统状态', dictType: 'sys_common_status', description: '通用状态' }
    await createDictTypeApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/dict-type', body)
  })

  it('更新字典类型：PUT /system/dict-type/{id}/{version}（dictType 回传原值）', async () => {
    const body = { dictName: '系统状态', dictType: 'sys_common_status' }
    await updateDictTypeApi(DICT_TYPE_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/dict-type/${DICT_TYPE_ID}/${VERSION}`, body)
  })

  it('删除字典类型：DELETE /system/dict-type/{id}/{version}', async () => {
    await deleteDictTypeApi(DICT_TYPE_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/dict-type/${DICT_TYPE_ID}/${VERSION}`)
  })

  it('分页查询字典数据：GET /system/dict-data/page 且 dictType/keyword 透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { dictType: 'sys_oper_type', keyword: '新增', pageIndex: 1, pageSize: 10 }
    await expect(getDictDataPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/dict-data/page', { params })
  })

  it('创建字典数据：POST /system/dict-data 且请求体透传', async () => {
    const body = {
      dictType: 'sys_oper_type',
      dictLabel: '新增',
      dictValue: '2',
      tagType: 2,
      orderNum: 1,
      status: 1,
    }
    await createDictDataApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/dict-data', body)
  })

  it('更新字典数据：PUT /system/dict-data/{id}/{version}', async () => {
    const body = {
      dictType: 'sys_oper_type',
      dictLabel: '修改',
      dictValue: '3',
      tagType: 4,
      orderNum: 2,
      status: 0,
    }
    await updateDictDataApi(DICT_DATA_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/dict-data/${DICT_DATA_ID}/${VERSION}`, body)
  })

  it('删除字典数据：DELETE /system/dict-data/{id}/{version}', async () => {
    await deleteDictDataApi(DICT_DATA_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/dict-data/${DICT_DATA_ID}/${VERSION}`)
  })

  it('按类型查询启用字典项：GET /system/dict-data/type/{dictType}', async () => {
    const items = [{ id: DICT_DATA_ID, dictValue: '1' }]
    mockedGet.mockResolvedValueOnce(items)
    await expect(getDictDataByTypeApi('sys_oper_type')).resolves.toBe(items)
    expect(mockedGet).toHaveBeenCalledWith('/system/dict-data/type/sys_oper_type')
  })
})
