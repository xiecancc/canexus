/**
 * 主题应用组合式函数。
 *
 * 将应用状态（暗色模式、主题色）同步到文档根元素：
 * - 暗色模式：切换 html.dark 类（配合 Element Plus dark css-vars）
 * - 主题色：写入 --el-color-primary 及其衍生变量（color-mix 混色，比例与 Element Plus 一致）
 *
 * 在 App.vue 初始化时调用一次；App 根组件常驻，副作用随应用生命周期存在。
 */
import { watchEffect } from 'vue'
import { DEFAULT_PRIMARY, useAppStore } from '@/stores/app'

/** 亮色模式混色基准（与 Element Plus 一致，向白色混合） */
const LIGHT_MIX_BASE = '#ffffff'

/** 暗色模式混色基准（与 Element Plus 暗黑主题背景一致） */
const DARK_MIX_BASE = '#141414'

/** 计算主题色相关的全部 CSS 变量 */
function buildPrimaryVars(primary: string, isDark: boolean): Record<string, string> {
  const base = isDark ? DARK_MIX_BASE : LIGHT_MIX_BASE
  // mix 百分比为基准色占比，与 Element Plus 官方衍生色比例一致
  const mix = (percent: number) => `color-mix(in srgb, ${base} ${percent}%, ${primary})`
  return {
    '--el-color-primary': primary,
    '--el-color-primary-light-3': mix(30),
    '--el-color-primary-light-5': mix(50),
    '--el-color-primary-light-7': mix(70),
    '--el-color-primary-light-8': mix(80),
    '--el-color-primary-light-9': mix(90),
    // dark-2：暗色模式向白色混合以增强对比（与 Element Plus 暗黑规则一致）
    '--el-color-primary-dark-2': isDark
      ? `color-mix(in srgb, #fff 20%, ${primary})`
      : `color-mix(in srgb, #000 20%, ${primary})`,
  }
}

/** 初始化主题：响应暗色模式与主题色变化，写入文档根元素 */
export function useTheme(): void {
  const appStore = useAppStore()

  watchEffect(() => {
    const root = document.documentElement
    // 同步暗黑模式类名（Element Plus dark css-vars 依赖 html.dark）
    root.classList.toggle('dark', appStore.isDark)
    // 写入主题色与衍生变量（内联样式优先级最高，覆盖样式表默认值）
    const vars = buildPrimaryVars(appStore.primary || DEFAULT_PRIMARY, appStore.isDark)
    for (const [key, value] of Object.entries(vars)) {
      root.style.setProperty(key, value)
    }
  })
}
