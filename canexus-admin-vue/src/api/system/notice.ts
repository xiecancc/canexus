/**
 * 通知公告模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/notice/page?title=&noticeType=&status=&pageIndex=&pageSize= → PagedResult<NoticeVO>
 * - GET    /system/notice/{id} → NoticeDetail（编辑回显 / 查看全文）
 * - POST   /system/notice → 创建公告
 * - PUT    /system/notice/{id} → 更新公告
 * - DELETE /system/notice/{id} → 删除公告
 * - GET    /system/notice/latest?count=5 → NoticeBrief[]（最新公告，首页卡片）
 * - 公告类型 noticeType：1 通知 2 公告（字典 sys_notice_type 尚未种子化，前端暂用本地常量）
 * - 状态 status：1 正常 0 关闭（契约无独立状态接口，行内切换经全量更新实现）
 */
import type { PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 通知公告列表项 */
export interface NoticeVO {
  /** 公告 ID（GUID 字符串） */
  id: string
  /** 公告标题 */
  title: string
  /** 公告类型：1 通知 2 公告 */
  noticeType: number
  /** 公告内容 */
  content: string
  /** 状态：1 正常 0 关闭 */
  status: number
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 通知公告分页查询参数 */
export interface NoticePageRequest extends PagedRequest {
  /** 公告标题（模糊匹配） */
  title?: string
  /** 公告类型：1 通知 2 公告 */
  noticeType?: number
  /** 状态：1 正常 0 关闭 */
  status?: number
}

/** 创建通知公告请求 */
export interface NoticeCreateRequest {
  /** 公告标题 */
  title: string
  /** 公告类型：1 通知 2 公告 */
  noticeType: number
  /** 公告内容 */
  content: string
  /** 状态：1 正常 0 关闭 */
  status: number
  /** 备注 */
  description?: string
}

/** 更新通知公告请求（字段与创建一致） */
export type NoticeUpdateRequest = NoticeCreateRequest

/** 最新公告简要信息（GET /system/notice/latest 返回项，不含内容） */
export interface NoticeBrief {
  /** 公告 ID（GUID 字符串） */
  id: string
  /** 公告标题 */
  title: string
  /** 公告类型：1 通知 2 公告 */
  noticeType: number
  /** 创建时间 */
  createdAt: string
}

/** 分页查询通知公告 */
export function getNoticePageApi(params: NoticePageRequest): Promise<PagedResult<NoticeVO>> {
  return request.get<PagedResult<NoticeVO>>('/system/notice/page', { params })
}

/** 查询通知公告详情 */
export function getNoticeApi(id: string): Promise<NoticeVO> {
  return request.get<NoticeVO>(`/system/notice/${id}`)
}

/** 创建通知公告 */
export function createNoticeApi(data: NoticeCreateRequest): Promise<null> {
  return request.post<null>('/system/notice', data)
}

/** 更新通知公告 */
export function updateNoticeApi(id: string, data: NoticeUpdateRequest): Promise<null> {
  return request.put<null>(`/system/notice/${id}`, data)
}

/** 删除通知公告 */
export function deleteNoticeApi(id: string): Promise<null> {
  return request.delete<null>(`/system/notice/${id}`)
}

/** 查询最新通知公告（首页卡片，缺省取 5 条） */
export function getLatestNoticesApi(count = 5): Promise<NoticeBrief[]> {
  return request.get<NoticeBrief[]>('/system/notice/latest', { params: { count } })
}
