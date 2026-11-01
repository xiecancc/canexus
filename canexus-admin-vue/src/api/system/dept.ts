/**
 * 系统部门模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/dept/tree?keyword=&status= → DeptVO[]（树结构）
 * - GET    /system/dept/{id}                  → DeptVO
 * - POST   /system/dept                       → 创建部门
 * - PUT    /system/dept/{id}                  → 更新部门
 * - DELETE /system/dept/{id}                  → 删除部门
 * - 顶级部门 parentId 约定为空 GUID（00000000-0000-0000-0000-000000000000）；状态：1 正常 0 停用
 */
import request from '@/utils/request'

/** 部门树节点 */
export interface DeptVO {
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
export function createDeptApi(data: DeptSaveRequest): Promise<null> {
  return request.post<null>('/system/dept', data)
}

/** 更新部门 */
export function updateDeptApi(id: string, data: DeptSaveRequest): Promise<null> {
  return request.put<null>(`/system/dept/${id}`, data)
}

/** 删除部门 */
export function deleteDeptApi(id: string): Promise<null> {
  return request.delete<null>(`/system/dept/${id}`)
}
