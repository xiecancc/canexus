/**
 * 图标数据源：来自 @element-plus/icons-vue 的导出名列表，供图标选择器与测试共用。
 */
import * as Icons from '@element-plus/icons-vue'

/** 全部图标名称（仅保留大写开头的组件导出） */
export const ALL_ICON_NAMES: string[] = Object.keys(Icons).filter((name) => /^[A-Z]/.test(name))

/** 按关键字过滤图标名称（忽略大小写的包含匹配，空关键字返回全部） */
export function filterIconNames(keyword: string): string[] {
  const key = keyword.trim().toLowerCase()
  if (!key) {
    return ALL_ICON_NAMES
  }
  return ALL_ICON_NAMES.filter((name) => name.toLowerCase().includes(key))
}
