/**
 * 字典渲染辅助：内置静态字典（状态、数据范围等）的标签与标签色取值。
 *
 * 动态字典统一经 useDict 拉取（如 sys_notice_type），本地常量仅作拉取失败兜底。
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { DICT_TAG_TYPE_MAP, useDict } from '@/composables/useDict'

/** 字典项（value 统一按字符串比较，兼容后端返回数字或字符串） */
export interface DictItem {
  /** 字典值 */
  value: string
  /** 显示标签 */
  label: string
  /** el-tag 类型 */
  tagType: 'primary' | 'success' | 'info' | 'warning' | 'danger'
}

/** 系统状态字典（sys_normal_disable）：1 正常 0 停用 */
export const SYS_NORMAL_DISABLE: DictItem[] = [
  { value: '1', label: '正常', tagType: 'success' },
  { value: '0', label: '停用', tagType: 'danger' },
]

/** 角色数据范围字典：1 全部 2 自定义 3 本部门 4 本部门及以下 5 仅本人 */
export const SYS_DATA_SCOPE: DictItem[] = [
  { value: '1', label: '全部数据权限', tagType: 'primary' },
  { value: '2', label: '自定义数据权限', tagType: 'warning' },
  { value: '3', label: '本部门数据权限', tagType: 'info' },
  { value: '4', label: '本部门及以下数据权限', tagType: 'info' },
  { value: '5', label: '仅本人数据权限', tagType: 'info' },
]

/** 菜单类型字典：1 目录 2 菜单 3 按钮 */
export const SYS_MENU_TYPE: DictItem[] = [
  { value: '1', label: '目录', tagType: 'primary' },
  { value: '2', label: '菜单', tagType: 'success' },
  { value: '3', label: '按钮', tagType: 'warning' },
]

/** 菜单可见性字典：1 显示 0 隐藏（契约未明确取值语义，与 status 保持同向约定，联调如有出入以联调为准） */
export const SYS_VISIBLE: DictItem[] = [
  { value: '1', label: '显示', tagType: 'success' },
  { value: '0', label: '隐藏', tagType: 'info' },
]

/**
 * 通知公告类型字典本地兜底常量：1 通知 2 公告。
 *
 * 正常情况使用 useNoticeTypeDict() 从 useDict('sys_notice_type') 动态获取；
 * 仅当动态字典未拉取到（后端字典缺失或请求失败）时回退本常量。
 */
export const SYS_NOTICE_TYPE: DictItem[] = [
  { value: '1', label: '通知', tagType: 'warning' },
  { value: '2', label: '公告', tagType: 'primary' },
]

/**
 * 通知公告类型字典（动态）：useDict('sys_notice_type') 优先，
 * 未拉取到时回退本地常量 SYS_NOTICE_TYPE，返回响应式字典项。
 */
export function useNoticeTypeDict(): ComputedRef<DictItem[]> {
  const { items } = useDict('sys_notice_type')
  return computed(() =>
    items.value.length
      ? items.value.map((item) => ({
          value: String(item.dictValue),
          label: item.dictLabel,
          tagType: DICT_TAG_TYPE_MAP[item.tagType] ?? 'info',
        }))
      : SYS_NOTICE_TYPE,
  )
}

/** 按值查找字典项 */
export function getDictItem(
  dict: DictItem[],
  value: string | number | null | undefined,
): DictItem | undefined {
  if (value === null || value === undefined || value === '') {
    return undefined
  }
  return dict.find((item) => item.value === String(value))
}

/** 按值获取字典标签（未匹配返回占位符） */
export function getDictLabel(
  dict: DictItem[],
  value: string | number | null | undefined,
  placeholder = '--',
): string {
  return getDictItem(dict, value)?.label ?? placeholder
}

/** 按值获取字典 el-tag 类型（未匹配返回 info） */
export function getDictTagType(
  dict: DictItem[],
  value: string | number | null | undefined,
): DictItem['tagType'] {
  return getDictItem(dict, value)?.tagType ?? 'info'
}
