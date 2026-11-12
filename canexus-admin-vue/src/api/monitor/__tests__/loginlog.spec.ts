/**
 * 登录日志模块接口测试：mock 请求层与下载层，验证接口路径、HTTP 方法与参数透传。
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

vi.mock('@/utils/download', () => ({
  downloadFile: vi.fn(),
}))

import request from '@/utils/request'
import { downloadFile } from '@/utils/download'
import {
  cleanLoginLogApi,
  deleteLoginLogApi,
  exportLoginLogApi,
  getLoginLogPageApi,
} from '../loginlog'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedDelete = request.delete as unknown as Mock
const mockedDownloadFile = downloadFile as unknown as Mock

describe('登录日志模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /system/login-log/page 且查询条件（含日期范围）透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = {
      userName: 'admin',
      ip: '127.0.0.1',
      status: 0,
      beginTime: '2026-02-01',
      endTime: '2026-02-28',
      pageIndex: 2,
      pageSize: 20,
    }
    await expect(getLoginLogPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/login-log/page', { params })
  })

  it('删除单条：DELETE /system/login-log/{id}/{version}', async () => {
    await deleteLoginLogApi(
      '00000000-0000-0000-0000-000000000011',
      '00000000-0000-0000-0000-00000000000a',
    )
    expect(mockedDelete).toHaveBeenCalledWith(
      '/system/login-log/00000000-0000-0000-0000-000000000011/00000000-0000-0000-0000-00000000000a',
    )
  })

  it('按时间范围清空：POST /system/login-log/clean', async () => {
    const params = { beginTime: '2026-11-01', endTime: '2026-11-07', confirm: true }
    await cleanLoginLogApi(params)
    expect(mockedPost).toHaveBeenCalledWith('/system/login-log/clean', null, { params })
  })

  it('导出 Excel：GET /system/login-log/export 且筛选条件透传（不含分页参数）', async () => {
    mockedDownloadFile.mockResolvedValueOnce(undefined)
    const params = {
      userName: 'admin',
      ip: '127.0.0.1',
      status: 0,
      beginTime: '2026-02-01',
      endTime: '2026-02-28',
    }
    await expect(exportLoginLogApi(params)).resolves.toBeUndefined()
    expect(mockedDownloadFile).toHaveBeenCalledWith(
      '/system/login-log/export',
      params,
      '登录日志.xlsx',
    )
    // 导出为文件流下载，不应走统一请求层
    expect(mockedGet).not.toHaveBeenCalled()
  })
})
