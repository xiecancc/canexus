/**
 * 岗位管理模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/post/page?postName=&postCode=&status=&pageIndex=&pageSize= → PagedResult<PostVO>
 * - GET    /system/post/{id}                                                  → PostDetail
 * - GET    /system/post/dropdown                                             → 岗位下拉（仅启用，用户管理页分配岗位用）
 * - POST   /system/post                                                      → 创建岗位
 * - PUT    /system/post/{id}/{version}                                       → 更新岗位
 * - DELETE /system/post/{id}/{version}                                       → 删除岗位（被用户引用时后端拒绝）
 * - PATCH  /system/post/{id}/{version}/restore                               → 恢复软删除岗位（菜单暂无该按钮权限，前端暂不接入）
 * - 状态 status：1 启用 0 停用
 * - 乐观锁：更新 / 删除的 URL 均须携带 version，取自列表响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 岗位列表项 */
export interface PostVO extends AggregateVO {
  /** 岗位 ID（GUID 字符串） */
  id: string
  /** 岗位名称 */
  postName: string
  /** 岗位编码 */
  postCode: string
  /** 显示顺序（升序） */
  orderNum: number
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 岗位详情（编辑回显） */
export interface PostDetail extends AggregateVO {
  /** 岗位 ID（GUID 字符串） */
  id: string
  /** 岗位名称 */
  postName: string
  /** 岗位编码 */
  postCode: string
  /** 显示顺序（升序） */
  orderNum: number
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description: string
}

/** 岗位分页查询参数 */
export interface PostPageRequest extends PagedRequest {
  /** 岗位名称（模糊匹配） */
  postName?: string
  /** 岗位编码（模糊匹配） */
  postCode?: string
  /** 状态：1 启用 0 停用 */
  status?: number
}

/** 创建 / 更新岗位请求（POST 与 PUT 共用载荷） */
export interface PostSaveRequest {
  /** 岗位名称（必填，不超过 50 字符） */
  postName: string
  /** 岗位编码（必填，不超过 64 字符，唯一） */
  postCode: string
  /** 显示顺序（升序，缺省 0） */
  orderNum: number
  /** 状态：1 启用 0 停用 */
  status: number
  /** 备注 */
  description?: string
}

/** 分页查询岗位 */
export function getPostPageApi(params: PostPageRequest): Promise<PagedResult<PostVO>> {
  return request.get<PagedResult<PostVO>>('/system/post/page', { params })
}

/** 查询岗位详情（编辑回显，含最新 version） */
export function getPostApi(id: string): Promise<PostDetail> {
  return request.get<PostDetail>(`/system/post/${id}`)
}

/** 创建岗位 */
export function createPostApi(data: PostSaveRequest): Promise<boolean> {
  return request.post<boolean>('/system/post', data)
}

/**
 * 更新岗位。
 *
 * @param id 岗位 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updatePostApi(
  id: string,
  version: string,
  data: PostSaveRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/post/${id}/${version}`, data)
}

/**
 * 删除岗位。
 *
 * @param id 岗位 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表响应的 version 字段）
 */
export function deletePostApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/post/${id}/${version}`)
}
