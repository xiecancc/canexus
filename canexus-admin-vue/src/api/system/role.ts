/**
 * 系统角色模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/role/page?keyword=&status=&pageIndex=&pageSize= → PagedResult<RoleVO>
 * - GET    /system/role/all                                       → 角色简要列表（下拉 / 复选）
 * - GET    /system/role/{id}                                      → 角色详情（含 menuIds）
 * - POST   /system/role                                           → 创建角色
 * - PUT    /system/role/{id}/{version}                            → 更新角色
 * - DELETE /system/role/{id}/{version}                            → 删除角色
 * - 状态：1 正常 0 停用；数据范围：1 全部 2 自定义 3 本部门 4 本部门及以下 5 仅本人
 * - 乐观锁：更新 / 删除的 URL 均须携带 version，取自列表 / 详情响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 角色列表项 */
export interface RoleVO extends AggregateVO {
  /** 角色 ID（GUID 字符串） */
  id: string
  /** 角色名称 */
  roleName: string
  /** 权限字符 */
  roleKey: string
  /** 数据范围：1 全部 2 自定义 3 本部门 4 本部门及以下 5 仅本人 */
  dataScope: number
  /** 状态：1 正常 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 角色简要信息（/system/role/all 返回项） */
export interface RoleBrief {
  /** 角色 ID（GUID 字符串） */
  id: string
  /** 角色名称 */
  roleName: string
  /** 权限字符 */
  roleKey: string
  /** 状态：1 正常 0 停用 */
  status: number
}

/** 角色详情（编辑回显，含授权菜单） */
export interface RoleDetail extends AggregateVO {
  /** 角色 ID（GUID 字符串） */
  id: string
  /** 角色名称 */
  roleName: string
  /** 权限字符 */
  roleKey: string
  /** 数据范围 */
  dataScope: number
  /** 状态：1 正常 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 授权菜单 ID 集合（全选节点集合，含父与子） */
  menuIds: string[]
}

/** 角色分页查询参数 */
export interface RolePageRequest extends PagedRequest {
  /** 关键字：匹配角色名称 / 权限字符 */
  keyword?: string
  /** 状态：1 正常 0 停用 */
  status?: number
}

/** 创建 / 更新角色请求（更新时 ID 位于路径） */
export interface RoleSaveRequest {
  /** 角色名称 */
  roleName: string
  /** 权限字符 */
  roleKey: string
  /** 数据范围 */
  dataScope: number
  /** 状态：1 正常 0 停用 */
  status: number
  /** 备注 */
  description?: string
  /** 授权菜单 ID 集合（含全选父级与半选父级） */
  menuIds: string[]
}

/** 分页查询角色 */
export function getRolePageApi(params: RolePageRequest): Promise<PagedResult<RoleVO>> {
  return request.get<PagedResult<RoleVO>>('/system/role/page', { params })
}

/** 查询全部角色（下拉 / 复选数据源） */
export function getAllRolesApi(): Promise<RoleBrief[]> {
  return request.get<RoleBrief[]>('/system/role/all')
}

/** 查询角色详情 */
export function getRoleApi(id: string): Promise<RoleDetail> {
  return request.get<RoleDetail>(`/system/role/${id}`)
}

/** 创建角色 */
export function createRoleApi(data: RoleSaveRequest): Promise<boolean> {
  return request.post<boolean>('/system/role', data)
}

/**
 * 更新角色。
 *
 * @param id 角色 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updateRoleApi(
  id: string,
  version: string,
  data: RoleSaveRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/role/${id}/${version}`, data)
}

/**
 * 删除角色。
 *
 * @param id 角色 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 */
export function deleteRoleApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/role/${id}/${version}`)
}
