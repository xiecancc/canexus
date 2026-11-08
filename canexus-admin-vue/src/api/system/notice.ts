/**
 * 通知公告模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/notice/page?title=&noticeType=&status=&pageIndex=&pageSize= → PagedResult<NoticeVO>
 * - GET    /system/notice/{id} → NoticeDetail（编辑回显 / 查看全文）
 * - POST   /system/notice → 创建公告
 * - PUT    /system/notice/{id}/{version} → 更新公告
 * - DELETE /system/notice/{id}/{version} → 删除公告
 * - GET    /system/notice/latest?count=5 → NoticeBrief[]（最新公告，首页卡片）
 * - 公告类型 noticeType：1 通知 2 公告（字典 sys_notice_type 尚未种子化，前端暂用本地常量）
 * - 状态 status：1 正常 0 关闭（契约无独立状态接口，行内切换经全量更新实现）
 * - 乐观锁：更新 / 删除的 URL 均须携带 version，取自列表 / 详情响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 通知公告列表项 */
export interface NoticeVO extends AggregateVO {
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
export function createNoticeApi(data: NoticeCreateRequest): Promise<boolean> {
  return request.post<boolean>('/system/notice', data)
}

/**
 * 更新通知公告。
 *
 * @param id 公告 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updateNoticeApi(
  id: string,
  version: string,
  data: NoticeUpdateRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/notice/${id}/${version}`, data)
}

/**
 * 删除通知公告。
 *
 * @param id 公告 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 */
export function deleteNoticeApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/notice/${id}/${version}`)
}

/** 查询最新通知公告（首页卡片，缺省取 5 条） */
export function getLatestNoticesApi(count = 5): Promise<NoticeBrief[]> {
  return request.get<NoticeBrief[]>('/system/notice/latest', { params: { count } })
}
