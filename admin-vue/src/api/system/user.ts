/**
 * 系统用户模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/user/page?keyword=&deptId=&status=&pageIndex=&pageSize= → PagedResult<UserVO>
 * - GET    /system/user/{id}                                              → UserDetail
 * - POST   /system/user                                                   → 创建用户
 * - PUT    /system/user/{id}                                              → 更新用户（不含用户名/密码）
 * - DELETE /system/user/{id}                                              → 删除用户
 * - PUT    /system/user/{id}/status                                       → 修改状态
 * - PUT    /system/user/{id}/password                                     → 重置密码
 * - PUT    /system/user/{id}/roles                                       → 分配角色
 * - GET    /system/user/import-template                                  → Blob（xlsx 模板文件流）
 * - POST   /system/user/import（FormData: file）                          → UserImportResult
 * - GET    /system/user/export?keyword=&deptId=&status=                   → Blob（xlsx 文件流）
 * - 用户状态：1 启用 0 停用
 * - 模板下载与数据导出为文件流接口，经 utils/download.ts 处理（不走 Result 解包）
 */
import type { PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 用户关联角色简要信息 */
export interface UserRoleBrief {
  /** 角色 ID（GUID 字符串） */
  id: string
  /** 角色名称 */
  roleName: string
}

/** 用户列表项 */
export interface UserVO {
  /** 用户 ID（GUID 字符串） */
  id: string
  /** 账号 */
  userName: string
  /** 昵称 */
  nickName: string
  /** 所属部门 ID（GUID 字符串） */
  deptId: string
  /** 所属部门名称 */
  deptName: string
  /** 邮箱 */
  email: string
  /** 手机号 */
  phone: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 创建时间 */
  createdAt: string
  /** 关联角色 */
  roles: UserRoleBrief[]
}

/** 用户详情（编辑回显） */
export interface UserDetail {
  /** 用户 ID（GUID 字符串） */
  id: string
  /** 账号 */
  userName: string
  /** 昵称 */
  nickName: string
  /** 所属部门 ID（GUID 字符串） */
  deptId: string
  /** 邮箱 */
  email: string
  /** 手机号 */
  phone: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 关联角色 ID 集合 */
  roleIds: string[]
}

/** 用户分页查询参数 */
export interface UserPageRequest extends PagedRequest {
  /** 关键字：匹配用户名 / 昵称 / 手机号 */
  keyword?: string
  /** 部门 ID（GUID 字符串） */
  deptId?: string
  /** 状态：1 启用 0 停用 */
  status?: number
}

/** 创建用户请求 */
export interface UserCreateRequest {
  /** 账号 */
  userName: string
  /** 昵称 */
  nickName: string
  /** 初始密码（缺省由后端取默认值） */
  password?: string
  /** 部门 ID（GUID 字符串） */
  deptId?: string
  /** 邮箱 */
  email?: string
  /** 手机号 */
  phone?: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description?: string
  /** 角色 ID 集合 */
  roleIds: string[]
}

/** 更新用户请求（不含账号与密码） */
export interface UserUpdateRequest {
  /** 昵称 */
  nickName: string
  /** 部门 ID（GUID 字符串） */
  deptId: string
  /** 邮箱 */
  email: string
  /** 手机号 */
  phone: string
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 角色 ID 集合 */
  roleIds: string[]
}

/** 分页查询用户 */
export function getUserPageApi(params: UserPageRequest): Promise<PagedResult<UserVO>> {
  return request.get<PagedResult<UserVO>>('/system/user/page', { params })
}

/** 查询用户详情 */
export function getUserApi(id: string): Promise<UserDetail> {
  return request.get<UserDetail>(`/system/user/${id}`)
}

/** 创建用户 */
export function createUserApi(data: UserCreateRequest): Promise<null> {
  return request.post<null>('/system/user', data)
}

/** 更新用户 */
export function updateUserApi(id: string, data: UserUpdateRequest): Promise<null> {
  return request.put<null>(`/system/user/${id}`, data)
}

/** 删除用户 */
export function deleteUserApi(id: string): Promise<null> {
  return request.delete<null>(`/system/user/${id}`)
}

/** 修改用户状态 */
export function updateUserStatusApi(id: string, status: number): Promise<null> {
  return request.put<null>(`/system/user/${id}/status`, { status })
}

/** 重置用户密码 */
export function updateUserPasswordApi(id: string, password: string): Promise<null> {
  return request.put<null>(`/system/user/${id}/password`, { password })
}

/** 分配用户角色 */
export function updateUserRolesApi(id: string, roleIds: string[]): Promise<null> {
  return request.put<null>(`/system/user/${id}/roles`, { roleIds })
}

/** 用户导入失败明细行 */
export interface UserImportFailureRow {
  /** 源数据行号（模板中的行号，从 2 起，首行为表头） */
  row: number
  /** 失败原因 */
  reason: string
}

/** 用户导入结果 */
export interface UserImportResult {
  /** 成功导入条数 */
  successCount: number
  /** 失败明细 */
  failureRows: UserImportFailureRow[]
}

/** 导入用户（FormData 仅含 file 字段，xlsx 文件） */
export function importUserApi(formData: FormData): Promise<UserImportResult> {
  return request.post<UserImportResult>('/system/user/import', formData)
}
