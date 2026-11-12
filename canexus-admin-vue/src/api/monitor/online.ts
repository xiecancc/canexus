/**
 * 在线会话模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET  /monitor/online/page?userName=&ip=&pageIndex=&pageSize= → PagedResult<OnlineUserVO>
 *   （一条活跃 refreshToken 记录代表一个在线会话，列表行的 id 即该会话记录 ID）
 * - POST /monitor/online/{tokenId}/revoke → 强制下线（吊销对应 refreshToken，无响应体）
 * - 权限：monitor:online:list / monitor:online:forceLogout
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 在线会话列表项 */
export interface OnlineUserVO extends AggregateVO {
  /** 会话记录 ID（GUID 字符串，即 refreshToken 记录 ID；强制下线时作为 tokenId 传入） */
  id: string
  /** 登录账号 */
  userName: string
  /** 用户昵称 */
  nickName: string
  /** 部门名称 */
  deptName: string
  /** 签发来源 IP */
  ip: string
  /** 签发来源 User-Agent 原文 */
  userAgent: string
  /** 登录（令牌签发）时间 */
  loginTime: string
  /** 会话过期时间（refreshToken 到期时间） */
  expiresAt: string
}

/** 在线会话分页查询参数 */
export interface OnlineUserPageRequest extends PagedRequest {
  /** 登录账号（模糊匹配） */
  userName?: string
  /** 登录 IP（模糊匹配） */
  ip?: string
}

/** 分页查询在线会话 */
export function getOnlineUserPageApi(
  params: OnlineUserPageRequest,
): Promise<PagedResult<OnlineUserVO>> {
  return request.get<PagedResult<OnlineUserVO>>('/monitor/online/page', { params })
}

/**
 * 强制下线指定在线会话（高危操作，后端留痕审计）。
 *
 * @param tokenId 会话记录 ID（取自列表行的 id 字段）
 */
export function forceLogoutApi(tokenId: string): Promise<null> {
  return request.post<null>(`/monitor/online/${tokenId}/revoke`)
}
