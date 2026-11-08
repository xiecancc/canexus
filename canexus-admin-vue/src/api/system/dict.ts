/**
 * 字典模块接口（字典类型 + 字典数据）与后端契约类型。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/dict-type/page?keyword=&pageIndex=&pageSize= → PagedResult<DictTypeVO>
 * - POST   /system/dict-type                                    → 创建字典类型
 * - PUT    /system/dict-type/{id}/{version}                     → 更新字典类型（dictType 不可改）
 * - DELETE /system/dict-type/{id}/{version}                     → 删除字典类型（存在字典数据时后端拒绝）
 * - GET    /system/dict-data/page?dictType=&keyword=&pageIndex=&pageSize= → PagedResult<DictDataVO>
 * - POST   /system/dict-data                                    → 创建字典数据
 * - PUT    /system/dict-data/{id}/{version}                     → 更新字典数据
 * - DELETE /system/dict-data/{id}/{version}                     → 删除字典数据
 * - GET    /system/dict-data/type/{dictType}                    → 启用状态字典数据列表（前端带缓存，见 useDict）
 * - tagType 标签色：1 primary 2 success 3 info 4 warning 5 danger
 * - status 状态：1 启用 0 停用
 * - 乐观锁：更新 / 删除的 URL 均须携带 version，取自列表 / 详情响应的 version 字段（36 位 GUID 字符串）
 */
import type { AggregateVO, PagedRequest, PagedResult } from '@/api/types'
import request from '@/utils/request'

/** 字典类型列表项 */
export interface DictTypeVO extends AggregateVO {
  /** 主键（GUID 字符串） */
  id: string
  /** 字典名称 */
  dictName: string
  /** 字典类型（全局唯一键，创建后不可改） */
  dictType: string
  /** 备注 */
  description: string
  /** 创建时间 */
  createdAt: string
}

/** 创建 / 更新字典类型请求（编辑时 dictType 不可改，仍需回传原值） */
export interface DictTypeSaveRequest {
  /** 字典名称 */
  dictName: string
  /** 字典类型 */
  dictType: string
  /** 备注 */
  description?: string
}

/** 字典类型分页查询参数 */
export interface DictTypePageRequest extends PagedRequest {
  /** 关键字：匹配字典名称 / 字典类型 */
  keyword?: string
}

/** 字典数据列表项 */
export interface DictDataVO extends AggregateVO {
  /** 主键（GUID 字符串） */
  id: string
  /** 所属字典类型 */
  dictType: string
  /** 字典标签（展示名） */
  dictLabel: string
  /** 字典键值（存储值） */
  dictValue: string
  /** 标签色：1 primary 2 success 3 info 4 warning 5 danger */
  tagType: number
  /** 备注 */
  description: string
  /** 显示排序 */
  orderNum: number
  /** 状态：1 启用 0 停用 */
  status: number
  /** 创建时间 */
  createdAt: string
}

/** 创建 / 更新字典数据请求 */
export interface DictDataSaveRequest {
  /** 所属字典类型 */
  dictType: string
  /** 字典标签 */
  dictLabel: string
  /** 字典键值 */
  dictValue: string
  /** 标签色：1 primary 2 success 3 info 4 warning 5 danger */
  tagType: number
  /** 备注 */
  description?: string
  /** 显示排序 */
  orderNum: number
  /** 状态：1 启用 0 停用 */
  status: number
}

/** 字典数据分页查询参数 */
export interface DictDataPageRequest extends PagedRequest {
  /** 字典类型（必传，按当前查看的类型过滤） */
  dictType: string
  /** 关键字：匹配字典标签 / 字典键值 */
  keyword?: string
}

/** 分页查询字典类型 */
export function getDictTypePageApi(params: DictTypePageRequest): Promise<PagedResult<DictTypeVO>> {
  return request.get<PagedResult<DictTypeVO>>('/system/dict-type/page', { params })
}

/** 创建字典类型 */
export function createDictTypeApi(data: DictTypeSaveRequest): Promise<boolean> {
  return request.post<boolean>('/system/dict-type', data)
}

/**
 * 更新字典类型（dictType 不可改，回传原值）。
 *
 * @param id 字典类型 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updateDictTypeApi(
  id: string,
  version: string,
  data: DictTypeSaveRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/dict-type/${id}/${version}`, data)
}

/**
 * 删除字典类型（存在字典数据时后端拒绝）。
 *
 * @param id 字典类型 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 */
export function deleteDictTypeApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/dict-type/${id}/${version}`)
}

/** 分页查询字典数据 */
export function getDictDataPageApi(params: DictDataPageRequest): Promise<PagedResult<DictDataVO>> {
  return request.get<PagedResult<DictDataVO>>('/system/dict-data/page', { params })
}

/** 创建字典数据 */
export function createDictDataApi(data: DictDataSaveRequest): Promise<boolean> {
  return request.post<boolean>('/system/dict-data', data)
}

/**
 * 更新字典数据。
 *
 * @param id 字典数据 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 * @param data 更新数据
 */
export function updateDictDataApi(
  id: string,
  version: string,
  data: DictDataSaveRequest,
): Promise<boolean> {
  return request.put<boolean>(`/system/dict-data/${id}/${version}`, data)
}

/**
 * 删除字典数据。
 *
 * @param id 字典数据 ID（GUID 字符串）
 * @param version 乐观锁版本号（取自列表 / 详情响应的 version 字段）
 */
export function deleteDictDataApi(id: string, version: string): Promise<boolean> {
  return request.delete<boolean>(`/system/dict-data/${id}/${version}`)
}

/** 按字典类型查询启用状态的字典数据列表（启用项，前端经 useDict 带缓存） */
export function getDictDataByTypeApi(dictType: string): Promise<DictDataVO[]> {
  return request.get<DictDataVO[]>(`/system/dict-data/type/${dictType}`)
}
