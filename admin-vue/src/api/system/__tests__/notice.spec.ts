/**
 * 通知公告模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
  createNoticeApi,
  deleteNoticeApi,
  getLatestNoticesApi,
  getNoticeApi,
  getNoticePageApi,
  updateNoticeApi,
} from '../notice'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用公告 ID（GUID 字符串） */
const NOTICE_ID = '00000000-0000-0000-0000-000000000006'

describe('通知公告模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询公告：GET /system/notice/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { title: '升级', noticeType: 1, status: 1, pageIndex: 1, pageSize: 10 }
    await expect(getNoticePageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/notice/page', { params })
  })

  it('查询公告详情：GET /system/notice/{id}', async () => {
    const detail = { id: NOTICE_ID, title: '系统升级公告', content: '今晚升级' }
    mockedGet.mockResolvedValueOnce(detail)
    await expect(getNoticeApi(NOTICE_ID)).resolves.toBe(detail)
    expect(mockedGet).toHaveBeenCalledWith(`/system/notice/${NOTICE_ID}`)
  })

  it('创建公告：POST /system/notice 且请求体透传', async () => {
    const body = { title: '新公告', noticeType: 2, content: '内容', status: 1 }
    await createNoticeApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/notice', body)
  })

  it('更新公告：PUT /system/notice/{id}', async () => {
    const body = { title: '改标题', noticeType: 1, content: '新内容', status: 0 }
    await updateNoticeApi(NOTICE_ID, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/notice/${NOTICE_ID}`, body)
  })

  it('删除公告：DELETE /system/notice/{id}', async () => {
    await deleteNoticeApi(NOTICE_ID)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/notice/${NOTICE_ID}`)
  })

  it('查询最新公告：GET /system/notice/latest 且 count 透传（缺省 5）', async () => {
    const latest = [
      { id: NOTICE_ID, title: '新公告', noticeType: 1, createdAt: '2026-01-01 08:00:00' },
    ]
    mockedGet.mockResolvedValueOnce(latest)
    await expect(getLatestNoticesApi()).resolves.toBe(latest)
    expect(mockedGet).toHaveBeenCalledWith('/system/notice/latest', { params: { count: 5 } })
  })
})
