/**
 * 部门模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import { createDeptApi, deleteDeptApi, getDeptApi, getDeptTreeApi, updateDeptApi } from '../dept'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 空 GUID（后端树根父 ID 约定，顶级部门以此为父） */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** 测试用部门 ID（GUID 字符串） */
const DEPT_ID = '00000000-0000-0000-0000-000000000004'

/** 测试用乐观锁版本号（GUID 字符串，取自列表 / 详情响应的 version 字段） */
const VERSION = '00000000-0000-0000-0000-00000000000a'

describe('部门模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('部门树查询：GET /system/dept/tree 且查询参数透传', async () => {
    await getDeptTreeApi({ keyword: '研发', status: 1 })
    expect(mockedGet).toHaveBeenCalledWith('/system/dept/tree', {
      params: { keyword: '研发', status: 1 },
    })
  })

  it('部门树查询：不传参数时请求 /system/dept/tree', async () => {
    await getDeptTreeApi()
    expect(mockedGet).toHaveBeenCalledWith('/system/dept/tree', { params: undefined })
  })

  it('部门详情：GET /system/dept/{id}', async () => {
    await getDeptApi(DEPT_ID)
    expect(mockedGet).toHaveBeenCalledWith(`/system/dept/${DEPT_ID}`)
  })

  it('创建部门：POST /system/dept 且顶级 parentId 为空 GUID', async () => {
    const body = {
      parentId: EMPTY_GUID,
      deptName: '研发部',
      orderNum: 1,
      leader: '',
      phone: '',
      email: '',
      status: 1,
    }
    await createDeptApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/dept', body)
  })

  it('更新部门：PUT /system/dept/{id}/{version}', async () => {
    const body = {
      parentId: EMPTY_GUID,
      deptName: '研发部',
      orderNum: 2,
      leader: '王五',
      phone: '',
      email: '',
      status: 1,
    }
    await updateDeptApi(DEPT_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/dept/${DEPT_ID}/${VERSION}`, body)
  })

  it('删除部门：DELETE /system/dept/{id}/{version}', async () => {
    await deleteDeptApi(DEPT_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/dept/${DEPT_ID}/${VERSION}`)
  })
})
