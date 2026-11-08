/**
 * 操作日志模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET /system/oper-log/page?title=&businessType=&status=&operUserName=&beginTime=&endTime=&pageIndex=&pageSize=
 *   → PagedResult<OperLogVO>（beginTime / endTime 为 'YYYY-MM-DD'，可空）
 * - DELETE /system/oper-log/{id}/{version} → 删除单条操作日志（菜单含 remove 按钮权限）
 * - POST /system/oper-log/clean?beginTime=&endTime=&confirm= → 按时间范围清空（须显式确认，单次最多 31 天）
 * - status 状态：1 成功 0 失败
 * - businessType 业务类型：来自字典 sys_oper_type
 * - 乐观锁：删除的 URL 须携带 version，取自列表响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 操作日志列表项 */
export interface OperLogVO extends AggregateVO {
  /** 主键（GUID 字符串） */
  id: string
  /** 模块标题（如"用户管理"） */
  title: string
  /** 业务类型（字典 sys_oper_type 的键值） */
  businessType: string
  /** 请求方法（后端方法全名） */
  method: string
  /** 请求方式（GET / POST 等） */
  requestMethod: string
  /** 请求 URL */
  requestUrl: string
  /** 请求参数（JSON 字符串） */
  operParam: string
  /** 操作 IP */
  operIp: string
  /** 操作人员 */
  operUserName: string
  /** 状态：1 成功 0 失败 */
  status: number
  /** 错误消息（失败时） */
  errorMsg: string
  /** 耗时（毫秒） */
  costMs: number
  /** 操作时间 */
  operTime: string
}

/** 操作日志分页查询参数 */
export interface OperLogPageRequest extends PagedRequest {
  /** 模块标题 */
  title?: string
  /** 业务类型（字典 sys_oper_type 键值） */
  businessType?: string
  /** 状态：1 成功 0 失败 */
  status?: number
  /** 操作人员 */
  operUserName?: string
  /** 开始日期（YYYY-MM-DD，含当天） */
  beginTime?: string
  /** 结束日期（YYYY-MM-DD，含当天） */
  endTime?: string
}

/** 分页查询操作日志 */
export function getOperLogPageApi(params: OperLogPageRequest): Promise<PagedResult<OperLogVO>> {
  return request.get<PagedResult<OperLogVO>>('/system/oper-log/page', { params })
}

/**
 * 删除单条操作日志。
 *
 * @param id 日志 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表响应的 version 字段）
 */
export function deleteOperLogApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/oper-log/${id}/${version}`)
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

/** 按时间范围清空操作日志（危险操作，后端按范围 DELETE 并记录审计） */
export function cleanOperLogApi(params: LogCleanParams): Promise<null> {
  return request.post<null>('/system/oper-log/clean', null, { params })
}
