/**
 * 定时任务模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /monitor/job/page?keyword=&status=&pageIndex=&pageSize= → PagedResult<JobVO>
 * - POST   /monitor/job → 创建任务
 * - PUT    /monitor/job/{id} → 更新任务
 * - DELETE /monitor/job/{id} → 删除任务
 * - PUT    /monitor/job/{id}/status body {status} → 修改任务状态
 * - PUT    /monitor/job/{id}/run → 立即执行一次任务
 * - GET    /monitor/job-log/page?jobId=&status=&pageIndex=&pageSize= → PagedResult<JobLogVO>
 * - 任务状态 status：1 运行 0 暂停
 * - 允许并发 concurrent：1 允许 0 禁止（若依惯例同向约定，联调如有出入以联调为准）
 */
import type { PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 定时任务列表项 */
export interface JobVO {
  /** 任务 ID（GUID 字符串） */
  id: string
  /** 任务名称 */
  jobName: string
  /** 任务分组 */
  jobGroup: string
  /** Cron 执行表达式 */
  cronExpression: string
  /** 任务类型（框架内置 Job 全名） */
  jobType: string
  /** 状态：1 运行 0 暂停 */
  status: number
  /** 允许并发：1 允许 0 禁止 */
  concurrent: number
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 定时任务分页查询参数 */
export interface JobPageRequest extends PagedRequest {
  /** 关键字：匹配任务名称 / 任务分组 */
  keyword?: string
  /** 状态：1 运行 0 暂停 */
  status?: number
}

/** 创建定时任务请求 */
export interface JobCreateRequest {
  /** 任务名称 */
  jobName: string
  /** 任务分组 */
  jobGroup: string
  /** Cron 执行表达式 */
  cronExpression: string
  /** 任务类型（框架内置 Job 全名） */
  jobType: string
  /** 状态：1 运行 0 暂停 */
  status: number
  /** 允许并发：1 允许 0 禁止 */
  concurrent: number
  /** 备注 */
  description?: string
}

/** 更新定时任务请求（字段与创建一致） */
export type JobUpdateRequest = JobCreateRequest

/** 任务执行日志列表项 */
export interface JobLogVO {
  /** 日志 ID（GUID 字符串） */
  id: string
  /** 任务 ID（GUID 字符串） */
  jobId: string
  /** 任务名称 */
  jobName: string
  /** 执行结果 */
  result: string
  /** 异常信息（失败时） */
  exception: string
  /** 耗时（毫秒） */
  costMs: number
  /** 执行状态：1 成功 0 失败 */
  status: number
  /** 执行时间 */
  triggerTime: string
}

/** 任务执行日志分页查询参数 */
export interface JobLogPageRequest extends PagedRequest {
  /** 任务 ID（GUID 字符串） */
  jobId?: string
  /** 执行状态：1 成功 0 失败 */
  status?: number
}

/** 分页查询定时任务 */
export function getJobPageApi(params: JobPageRequest): Promise<PagedResult<JobVO>> {
  return request.get<PagedResult<JobVO>>('/monitor/job/page', { params })
}

/** 创建定时任务 */
export function createJobApi(data: JobCreateRequest): Promise<null> {
  return request.post<null>('/monitor/job', data)
}

/** 更新定时任务 */
export function updateJobApi(id: string, data: JobUpdateRequest): Promise<null> {
  return request.put<null>(`/monitor/job/${id}`, data)
}

/** 删除定时任务 */
export function deleteJobApi(id: string): Promise<null> {
  return request.delete<null>(`/monitor/job/${id}`)
}

/** 修改定时任务状态（1 运行 0 暂停） */
export function updateJobStatusApi(id: string, status: number): Promise<null> {
  return request.put<null>(`/monitor/job/${id}/status`, { status })
}

/** 立即执行一次定时任务 */
export function runJobApi(id: string): Promise<null> {
  return request.put<null>(`/monitor/job/${id}/run`)
}

/** 分页查询任务执行日志 */
export function getJobLogPageApi(params: JobLogPageRequest): Promise<PagedResult<JobLogVO>> {
  return request.get<PagedResult<JobLogVO>>('/monitor/job-log/page', { params })
}
