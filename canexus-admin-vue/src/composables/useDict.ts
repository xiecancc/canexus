/**
 * 动态字典组合式函数：按字典类型拉取启用状态的字典数据并做模块级缓存。
 *
 * - 缓存 Map<dictType, Ref<DictItem[]>>：同类型多处使用共享同一份响应式数据；
 * - 同类型并发调用共享同一次请求（进行中任务表），避免重复请求；
 * - refresh() 强制重新拉取（字典数据维护后调用），失败后缓存保留为空，
 *   下次挂载或 refresh 可重试；错误提示由请求层统一处理。
 */
import { ref } from 'vue'
import type { Ref } from 'vue'
import { getDictDataByTypeApi } from '@/api/system/dict'
import type { DictDataVO } from '@/api/system/dict'

/** 字典数据项（useDict 返回的动态字典条目） */
export type DictItem = DictDataVO

/** el-tag 类型名 */
export type TagTypeName = 'primary' | 'success' | 'info' | 'warning' | 'danger'

/** 动态字典 tagType（后端数字 1-5）到 el-tag 类型的映射 */
export const DICT_TAG_TYPE_MAP: Record<number, TagTypeName> = {
  1: 'primary',
  2: 'success',
  3: 'info',
  4: 'warning',
  5: 'danger',
}

/** 模块级字典缓存：dictType → 响应式字典项数组 */
const dictCache = new Map<string, Ref<DictItem[]>>()

/** 进行中的拉取任务：dictType → Promise（同类型并发共享） */
const pendingTasks = new Map<string, Promise<void>>()

/** 清空字典缓存（登出或测试重置用；清空后下次 useDict 会重新拉取） */
export function clearDictCache(): void {
  dictCache.clear()
  pendingTasks.clear()
}

/** 拉取指定类型字典并写入缓存（完成后移除进行中任务） */
async function fetchDict(dictType: string, items: Ref<DictItem[]>): Promise<void> {
  try {
    items.value = (await getDictDataByTypeApi(dictType)) ?? []
  } finally {
    pendingTasks.delete(dictType)
  }
}

/** 动态字典组合式函数：返回 { items 字典项响应式数组, refresh 强制刷新 } */
export function useDict(dictType: string) {
  let items = dictCache.get(dictType)
  if (!items) {
    items = ref<DictItem[]>([])
    dictCache.set(dictType, items)
  }

  /** 强制刷新字典缓存（同类型进行中的请求会被共享等待） */
  async function refresh(): Promise<void> {
    const pending = pendingTasks.get(dictType)
    if (pending) {
      await pending
      return
    }
    const task = fetchDict(dictType, items as Ref<DictItem[]>)
    pendingTasks.set(dictType, task)
    await task
  }

  // 缓存为空且无进行中任务时立即拉取（失败静默，错误提示由请求层处理，可重试）
  if (items.value.length === 0 && !pendingTasks.has(dictType)) {
    void refresh().catch(() => undefined)
  }

  return { items, refresh }
}
