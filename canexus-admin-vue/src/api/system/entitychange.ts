/**
 * 实体变更日志模块接口与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET /system/entity-change/page?entityName=&entityId=&fieldName=&pageIndex=&pageSize=
 *   → PagedResult<EntityChangeLogVO>（只读，写入由聚合仓储更新链路自动完成）
 * - 权限：monitor:changelog:list（后端 SysEntityChangeLogController）
 * - 仅提供分页查询，无详情 / 删除 / 导出端点
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 实体变更日志列表项 */
export interface EntityChangeLogVO extends AggregateVO {
  /** 日志 ID（GUID 字符串） */
  id: string
  /** 变更的实体类型名（如 SysUser） */
  entityName: string
  /** 变更的实体主键 ID（GUID 字符串） */
  entityId: string
  /** 变更的字段名 */
  fieldName: string
  /** 变更前的值 */
  oldValue: string
  /** 变更后的值 */
  newValue: string
  /** 变更时间 */
  changedAt: string
  /** 变更人用户 ID（GUID 字符串；系统任务为空） */
  changedBy: string
}

/** 实体变更日志分页查询参数 */
export interface EntityChangeLogPageRequest extends PagedRequest {
  /** 实体类型名（模糊匹配） */
  entityName?: string
  /** 实体主键 ID（精确匹配，GUID 字符串） */
  entityId?: string
  /** 字段名（模糊匹配） */
  fieldName?: string
}

/** 分页查询实体变更日志 */
export function getEntityChangeLogPageApi(
  params: EntityChangeLogPageRequest,
): Promise<PagedResult<EntityChangeLogVO>> {
  return request.get<PagedResult<EntityChangeLogVO>>('/system/entity-change/page', { params })
}
