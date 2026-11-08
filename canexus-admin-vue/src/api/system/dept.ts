/**
 * 系统部门模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/dept/tree?keyword=&status= → DeptVO[]（树结构）
 * - GET    /system/dept/{id}                  → DeptVO
 * - POST   /system/dept                       → 创建部门
 * - PUT    /system/dept/{id}/{version}        → 更新部门
 * - DELETE /system/dept/{id}/{version}        → 删除部门
 * - 顶级部门 parentId 约定为空 GUID（00000000-0000-0000-0000-000000000000）；状态：1 正常 0 停用
 * - 乐观锁：更新 / 删除的 URL 均须携带 version，取自列表 / 详情响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO } from '@/api/types'
import request from '@/utils/request'

/** 部门树节点 */
export interface DeptVO extends AggregateVO {
  /** 部门 ID（GUID 字符串） */
  id: string
  /** 父部门 ID（顶级为空 GUID：00000000-0000-0000-0000-000000000000） */
  parentId: string
  /** 部门名称 */
  deptName: string
  /** 显示排序（越小越靠前） */
  orderNum: number
  /** 负责人 */
  leader: string
  /** 联系电话 */
  phone: string
  /** 邮箱 */
  email: string
  /** 状态：1 正常 0 停用 */
  status: number
  /** 创建时间 */
  createdAt: string
  /** 子部门 */
  children: DeptVO[]
}

/** 部门树查询参数 */
export interface DeptTreeRequest {
  /** 关键字：匹配部门名称 */
  keyword?: string
  /** 状态：1 正常 0 停用 */
  status?: number
}

/** 创建部门请求 */
export interface DeptSaveRequest {
  /** 父部门 ID（顶级为空 GUID：00000000-0000-0000-0000-000000000000） */
  parentId: string
  /** 部门名称 */
  deptName: string
  /** 显示排序 */
  orderNum: number
  /** 负责人 */
  leader?: string
  /** 联系电话 */
  phone?: string
  /** 邮箱 */
  email?: string
  /** 状态：1 正常 0 停用 */
  status: number
}

/** 查询部门树 */
export function getDeptTreeApi(params?: DeptTreeRequest): Promise<DeptVO[]> {
  return request.get<DeptVO[]>('/system/dept/tree', { params })
}

/** 查询部门详情 */
export function getDeptApi(id: string): Promise<DeptVO> {
  return request.get<DeptVO>(`/system/dept/${id}`)
}

/** 创建部门 */
export function createDeptApi(data: DeptSaveRequest): Promise<boolean> {
  return request.post<boolean>('/system/dept', data)
}

/**
 * 更新部门。
 *
 * @param id 部门 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updateDeptApi(
  id: string,
  version: string,
  data: DeptSaveRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/dept/${id}/${version}`, data)
}

/**
 * 删除部门。
 *
 * @param id 部门 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 */
export function deleteDeptApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/dept/${id}/${version}`)
}
