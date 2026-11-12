/**
 * 岗位管理模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import { createPostApi, deletePostApi, getPostApi, getPostPageApi, updatePostApi } from '../post'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用岗位 ID（GUID 字符串） */
const POST_ID = '00000000-0000-0000-0000-000000000005'

/** 测试用乐观锁版本号（GUID 字符串，取自列表 / 详情响应的 version 字段） */
const VERSION = '00000000-0000-0000-0000-00000000000a'

describe('岗位管理模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询岗位：GET /system/post/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { postName: '经理', postCode: 'MGR', status: 1, pageIndex: 2, pageSize: 20 }
    await expect(getPostPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/post/page', { params })
  })

  it('查询岗位详情：GET /system/post/{id}', async () => {
    mockedGet.mockResolvedValueOnce({ id: POST_ID, version: VERSION, postName: '经理' })
    await expect(getPostApi(POST_ID)).resolves.toMatchObject({ id: POST_ID })
    expect(mockedGet).toHaveBeenCalledWith(`/system/post/${POST_ID}`)
  })

  it('创建岗位：POST /system/post 且请求体透传', async () => {
    const body = { postName: '经理', postCode: 'MGR', orderNum: 1, status: 1 }
    await createPostApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/post', body)
  })

  it('更新岗位：PUT /system/post/{id}/{version}', async () => {
    const body = { postName: '高级经理', postCode: 'MGR', orderNum: 1, status: 1 }
    await updatePostApi(POST_ID, VERSION, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/post/${POST_ID}/${VERSION}`, body)
  })

  it('删除岗位：DELETE /system/post/{id}/{version}', async () => {
    await deletePostApi(POST_ID, VERSION)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/post/${POST_ID}/${VERSION}`)
  })
})
