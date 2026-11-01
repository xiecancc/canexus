/**
 * 应用全局状态：侧边栏、设备类型、主题（暗色模式 / 主题色）。
 *
 * 关键状态通过 useStorage 持久化到 localStorage（统一 super_admin_ 前缀）。
 */
import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { usePreferredDark, useStorage } from '@vueuse/core'

/** 主题模式：亮色 / 暗色 / 跟随系统 */
export type ThemeMode = 'light' | 'dark' | 'auto'

/** 设备类型 */
export type DeviceType = 'desktop' | 'mobile'

/** 主题模式存储键 */
const THEME_MODE_KEY = 'super_admin_theme_mode'

/** 主题色存储键 */
const PRIMARY_KEY = 'super_admin_primary'

/** 侧边栏折叠状态存储键 */
const SIDEBAR_COLLAPSED_KEY = 'super_admin_sidebar_collapsed'

/** 默认主题色（Element Plus 默认蓝） */
export const DEFAULT_PRIMARY = '#409eff'

/** 应用全局状态 */
export const useAppStore = defineStore('app', () => {
  /** 侧边栏是否折叠（桌面端为收起，移动端为隐藏） */
  const sidebarCollapsed = useStorage<boolean>(SIDEBAR_COLLAPSED_KEY, false, undefined, {
    flush: 'sync',
  })

  /** 当前设备类型 */
  const device = ref<DeviceType>('desktop')

  /** 主题模式（持久化，默认跟随系统） */
  const themeMode = useStorage<ThemeMode>(THEME_MODE_KEY, 'auto', undefined, { flush: 'sync' })

  /** 系统是否偏好暗色（themeMode 为 auto 时生效） */
  const prefersDark = usePreferredDark()

  /** 是否处于暗色模式（可写计算属性，赋值时自动落盘为 light/dark） */
  const isDark = computed({
    get: () => (themeMode.value === 'auto' ? prefersDark.value : themeMode.value === 'dark'),
    set: (value: boolean) => {
      themeMode.value = value ? 'dark' : 'light'
    },
  })

  /** 主题色（持久化） */
  const primary = useStorage<string>(PRIMARY_KEY, DEFAULT_PRIMARY, undefined, { flush: 'sync' })

  /** 设置主题模式 */
  function setThemeMode(mode: ThemeMode): void {
    themeMode.value = mode
  }

  /** 切换暗色模式 */
  function toggleDark(): void {
    isDark.value = !isDark.value
  }

  /** 设置主题色 */
  function setPrimary(color: string): void {
    primary.value = color
  }

  /** 切换侧边栏折叠 */
  function toggleSidebar(): void {
    sidebarCollapsed.value = !sidebarCollapsed.value
  }

  return {
    sidebarCollapsed,
    device,
    themeMode,
    isDark,
    primary,
    setThemeMode,
    toggleDark,
    setPrimary,
    toggleSidebar,
  }
})
