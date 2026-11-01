/**
 * 与后端契约对应的通用类型定义。
 *
 * 后端所有接口统一返回 `{ code, message, data }`，code === 200 表示成功。
 */

/** 统一响应结构 */
export interface Result<T = unknown> {
  /** 业务状态码，200 表示成功 */
  code: number
  /** 提示消息 */
  message: string
  /** 业务数据 */
  data: T
}

/** 分页请求参数（pageIndex 从 1 开始） */
export interface PagedRequest {
  /** 页码，从 1 开始 */
  pageIndex: number
  /** 每页条数 */
  pageSize: number
}

/** 分页响应结构 */
export interface PagedResult<T = unknown> {
  /** 当前页数据 */
  items: T[]
  /** 总条数 */
  total: number
  /** 当前页码 */
  pageIndex: number
  /** 每页条数 */
  pageSize: number
}
