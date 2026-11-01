/**
 * 参数配置模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/config/page?keyword=&pageIndex=&pageSize= → PagedResult<ConfigVO>
 * - GET    /system/config/key/{configKey}                    → data 为参数值字符串
 * - POST   /system/config                                    → 创建参数
 * - PUT    /system/config/{id}                               → 更新参数
 * - DELETE /system/config/{id}                               → 删除参数（builtIn 内置参数后端拒绝删除，前端禁用按钮）
 */
import type { PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 参数配置列表项 */
export interface ConfigVO {
  /** 主键（GUID 字符串） */
  id: string
  /** 参数名称 */
  configName: string
  /** 参数键名（全局唯一，供业务读取） */
  configKey: string
  /** 参数键值 */
  configValue: string
  /** 是否内置（内置参数不可删除） */
  builtIn: boolean
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 参数分页查询参数 */
export interface ConfigPageRequest extends PagedRequest {
  /** 关键字：匹配参数名称 / 参数键名 */
  keyword?: string
}

/** 创建 / 更新参数请求 */
export interface ConfigSaveRequest {
  /** 参数名称 */
  configName: string
  /** 参数键名 */
  configKey: string
  /** 参数键值 */
  configValue: string
  /** 备注 */
  description?: string
}

/** 分页查询参数配置 */
export function getConfigPageApi(params: ConfigPageRequest): Promise<PagedResult<ConfigVO>> {
  return request.get<PagedResult<ConfigVO>>('/system/config/page', { params })
}

/** 按参数键名查询参数值（data 为字符串） */
export function getConfigByKeyApi(configKey: string): Promise<string> {
  return request.get<string>(`/system/config/key/${configKey}`)
}

/** 创建参数 */
export function createConfigApi(data: ConfigSaveRequest): Promise<null> {
  return request.post<null>('/system/config', data)
}

/** 更新参数 */
export function updateConfigApi(id: string, data: ConfigSaveRequest): Promise<null> {
  return request.put<null>(`/system/config/${id}`, data)
}

/** 删除参数（builtIn 内置参数后端拒绝） */
export function deleteConfigApi(id: string): Promise<null> {
  return request.delete<null>(`/system/config/${id}`)
}
