/**
 * 与后端契约对应的通用类型定义。
 *
 * 后端成功响应为 `{ code, message, data }`，code === 200 表示成功；响应拦截器解包后直接返回 data。
 * - 聚合写命令（创建 / 更新 / 删除 / 恢复 / 状态 / 重置密码 / 分配角色）的 data 为 boolean，
 *   表示写命令是否生效；恢复的幂等 no-op 返回 `false`，但仍是 code 200，不得当作业务失败。
 * - 无返回值命令（登出 / 立即执行任务 / 清空日志 / 强制下线）与全部错误响应不含 data 字段，
 *   对应接口声明为 `Promise<null>`，解包结果为 undefined。
 */

/**
 * 聚合根通用字段：后端列表与详情响应均携带乐观锁版本号。
 *
 * 后端所有聚合写命令（更新 / 删除 / 恢复 / 状态 / 重置密码 / 分配角色等）
 * 的路由形如 `/xxx/{id}/{version}`，version 段须取自最近一次读取到的聚合数据。
 */
export interface AggregateVO {
  /** 乐观锁版本号（36 位 GUID 字符串，取自列表 / 详情响应的 version 字段） */
  version: string
}

/** 统一响应结构 */
export interface Result<T = unknown> {
  /** 业务状态码，200 表示成功 */
  code: number
  /** 提示消息 */
  message: string
  /** 业务数据（无载荷响应省略该字段） */
  data?: T
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
