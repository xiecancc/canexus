/**
 * 租户管理模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/tenant/page?keyword=&status=&pageIndex=&pageSize= → PagedResult<TenantVO>
 * - GET    /system/tenant/all → TenantBrief[]（租户简要集合，供下拉选择）
 * - POST   /system/tenant → 创建租户
 * - PUT    /system/tenant/{id} → 更新租户（tenantKey 编辑禁用）
 * - DELETE /system/tenant/{id} → 删除租户
 * - 租户状态：1 启用 0 停用（契约无独立状态接口，行内切换经全量更新实现）
 */
import type { PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 租户列表项 */
export interface TenantVO {
  /** 租户 ID（GUID 字符串） */
  id: string
  /** 租户标识（唯一） */
  tenantKey: string
  /** 租户名称 */
  tenantName: string
  /** 联系电话 */
  contactPhone: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 过期时间（空表示长期有效） */
  expireTime: string | null
  /** 创建时间 */
  createdAt: string
}

/** 租户简要信息（GET /system/tenant/all 返回项） */
export interface TenantBrief {
  /** 租户 ID（GUID 字符串） */
  id: string
  /** 租户标识 */
  tenantKey: string
  /** 租户名称 */
  tenantName: string
  /** 状态：1 启用 0 停用 */
  status: number
}

/** 租户分页查询参数 */
export interface TenantPageRequest extends PagedRequest {
  /** 关键字：匹配租户标识 / 租户名称 */
  keyword?: string
  /** 状态：1 启用 0 停用 */
  status?: number
}

/** 创建租户请求 */
export interface TenantCreateRequest {
  /** 租户标识（唯一） */
  tenantKey: string
  /** 租户名称 */
  tenantName: string
  /** 联系电话 */
  contactPhone?: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description?: string
  /** 过期时间（空表示长期有效） */
  expireTime?: string
}

/** 更新租户请求（tenantKey 编辑禁用，仍需随请求提交） */
export interface TenantUpdateRequest {
  /** 租户标识（唯一，编辑时不可修改） */
  tenantKey: string
  /** 租户名称 */
  tenantName: string
  /** 联系电话 */
  contactPhone?: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description?: string
  /** 过期时间（空表示长期有效） */
  expireTime?: string
}

/** 分页查询租户 */
export function getTenantPageApi(params: TenantPageRequest): Promise<PagedResult<TenantVO>> {
  return request.get<PagedResult<TenantVO>>('/system/tenant/page', { params })
}

/** 查询全部租户简要信息（下拉选择用） */
export function getTenantAllApi(): Promise<TenantBrief[]> {
  return request.get<TenantBrief[]>('/system/tenant/all')
}

/** 创建租户 */
export function createTenantApi(data: TenantCreateRequest): Promise<null> {
  return request.post<null>('/system/tenant', data)
}

/** 更新租户（tenantKey 不可修改） */
export function updateTenantApi(id: string, data: TenantUpdateRequest): Promise<null> {
  return request.put<null>(`/system/tenant/${id}`, data)
}

/** 删除租户 */
export function deleteTenantApi(id: string): Promise<null> {
  return request.delete<null>(`/system/tenant/${id}`)
}
