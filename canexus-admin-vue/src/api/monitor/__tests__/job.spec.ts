/**
 * 定时任务模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
  createJobApi,
  deleteJobApi,
  getJobLogPageApi,
  getJobPageApi,
  runJobApi,
  updateJobApi,
  updateJobStatusApi,
} from '../job'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用任务 ID（GUID 字符串） */
const JOB_ID = '00000000-0000-0000-0000-000000000003'

describe('定时任务模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询任务：GET /monitor/job/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { keyword: '数据清理', status: 1, pageIndex: 1, pageSize: 10 }
    await expect(getJobPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/monitor/job/page', { params })
  })

  it('创建任务：POST /monitor/job 且请求体透传', async () => {
    const body = {
      jobName: '数据清理',
      jobGroup: 'DEFAULT',
      cronExpression: '0 0/30 * * * ?',
      jobType: 'System.Jobs.JobCleanJob',
      status: 0,
      concurrent: 1,
    }
    await createJobApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/monitor/job', body)
  })

  it('更新任务：PUT /monitor/job/{id}', async () => {
    const body = {
      jobName: '数据清理',
      jobGroup: 'DEFAULT',
      cronExpression: '0 0 1 * * ?',
      jobType: 'System.Jobs.JobCleanJob',
      status: 1,
      concurrent: 0,
    }
    await updateJobApi(JOB_ID, body)
    expect(mockedPut).toHaveBeenCalledWith(`/monitor/job/${JOB_ID}`, body)
  })

  it('删除任务：DELETE /monitor/job/{id}', async () => {
    await deleteJobApi(JOB_ID)
    expect(mockedDelete).toHaveBeenCalledWith(`/monitor/job/${JOB_ID}`)
  })

  it('修改任务状态：PUT /monitor/job/{id}/status 且 body 透传 {status}', async () => {
    await updateJobStatusApi(JOB_ID, 1)
    expect(mockedPut).toHaveBeenCalledWith(`/monitor/job/${JOB_ID}/status`, { status: 1 })
  })

  it('立即执行任务：PUT /monitor/job/{id}/run', async () => {
    await runJobApi(JOB_ID)
    expect(mockedPut).toHaveBeenCalledWith(`/monitor/job/${JOB_ID}/run`)
  })

  it('分页查询执行日志：GET /monitor/job-log/page 且 jobId / status 透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { jobId: JOB_ID, status: 0, pageIndex: 2, pageSize: 10 }
    await expect(getJobLogPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/monitor/job-log/page', { params })
  })
})
