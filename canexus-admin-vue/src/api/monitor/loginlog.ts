/**
 * 登录日志模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET /system/login-log/page?userName=&ip=&status=&beginTime=&endTime=&pageIndex=&pageSize=
 *   → PagedResult<LoginLogVO>（beginTime / endTime 为 'YYYY-MM-DD'，可空）
 * - DELETE /system/login-log/{id}/{version} → 删除单条登录日志（菜单含 remove 按钮权限）
 * - POST /system/login-log/clean?beginTime=&endTime=&confirm= → 按时间范围清空（须显式确认，单次最多 31 天）
 * - GET /system/login-log/export?userName=&ip=&status=&beginTime=&endTime=
 *   → xlsx 文件流（复用列表筛选条件，分页参数被忽略，行数上限 10000；经 utils/download.ts 处理）
 * - status 状态：1 成功 0 失败（与后端 SysLoginLog 实体取值一致）
 * - 乐观锁：删除的 URL 须携带 version，取自列表响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import { downloadFile } from '@/utils/download'
import request from '@/utils/request'

/** 登录日志列表项 */
export interface LoginLogVO extends AggregateVO {
  /** 主键（GUID 字符串） */
  id: string
  /** 登录账号 */
  userName: string
  /** 登录 IP */
  ip: string
  /** 登录地点 */
  loginLocation: string
  /** 原始 User-Agent */
  userAgent: string
  /** 登录状态：1 成功 0 失败 */
  status: number
  /** 提示消息（如"登录成功""密码错误"） */
  msg: string
  /** 登录时间 */
  loginTime: string
}

/** 登录日志分页查询参数 */
export interface LoginLogPageRequest extends PagedRequest {
  /** 登录账号 */
  userName?: string
  /** 登录 IP */
  ip?: string
  /** 登录状态：1 成功 0 失败 */
  status?: number
  /** 开始日期（YYYY-MM-DD，含当天） */
  beginTime?: string
  /** 结束日期（YYYY-MM-DD，含当天） */
  endTime?: string
}

/** 分页查询登录日志 */
export function getLoginLogPageApi(params: LoginLogPageRequest): Promise<PagedResult<LoginLogVO>> {
  return request.get<PagedResult<LoginLogVO>>('/system/login-log/page', { params })
}

/**
 * 删除单条登录日志。
 *
 * @param id 日志 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表响应的 version 字段）
 */
export function deleteLoginLogApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/login-log/${id}/${version}`)
}

/** 日志清空参数（后端要求明确时间范围与显式确认，单次最多 31 天） */
export interface LogCleanParams {
  /** 起始日期 YYYY-MM-DD（含当天） */
  beginTime: string
  /** 结束日期 YYYY-MM-DD（含当天） */
  endTime: string
  /** 显式确认标志（必须为 true） */
  confirm: boolean
}

/** 按时间范围清空登录日志（危险操作，后端按范围 DELETE 并记录审计） */
export function cleanLoginLogApi(params: LogCleanParams): Promise<null> {
  return request.post<null>('/system/login-log/clean', null, { params })
}

/** 登录日志导出筛选条件（与列表同口径；后端忽略分页参数，全量按筛选导出） */
export type LoginLogExportRequest = Omit<LoginLogPageRequest, 'pageIndex' | 'pageSize'>

/**
 * 导出登录日志为 Excel（GET /system/login-log/export）。
 *
 * 后端返回 xlsx 文件流并在 Content-Disposition 携带建议文件名，经 utils/download.ts
 * 以 `responseType: 'blob'` 下载，不走统一 Result 解包拦截器。
 *
 * @param params 与列表一致的筛选条件（userName / ip / status / beginTime / endTime）
 */
export function exportLoginLogApi(params: LoginLogExportRequest): Promise<void> {
  const query: Record<string, unknown> = { ...params }
  return downloadFile('/system/login-log/export', query, '登录日志.xlsx')
}
