import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { DEFAULT_PRIMARY, useAppStore } from '../app'

describe('useAppStore 应用状态', () => {
  beforeAll(() => {
    // jsdom 未实现 matchMedia，VueUse usePreferredDark 依赖该 API，这里补充最小桩实现
    if (!window.matchMedia) {
      Object.defineProperty(window, 'matchMedia', {
        writable: true,
        value: (query: string) => ({
          matches: false,
          media: query,
          onchange: null,
          addListener: () => {},
          removeListener: () => {},
          addEventListener: () => {},
          removeEventListener: () => {},
          dispatchEvent: () => false,
        }),
      })
    }
  })

  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('默认主题色为 Element Plus 蓝，主题模式跟随系统', () => {
    const store = useAppStore()
    expect(store.primary).toBe(DEFAULT_PRIMARY)
    expect(store.themeMode).toBe('auto')
  })

  it('toggleDark 在亮暗之间切换并同步 themeMode', () => {
    const store = useAppStore()
    store.setThemeMode('light')
    expect(store.isDark).toBe(false)
    store.toggleDark()
    expect(store.themeMode).toBe('dark')
    expect(store.isDark).toBe(true)
    store.toggleDark()
    expect(store.themeMode).toBe('light')
    expect(store.isDark).toBe(false)
  })

  it('setThemeMode 支持跟随系统', () => {
    const store = useAppStore()
    store.setThemeMode('auto')
    expect(store.themeMode).toBe('auto')
  })

  it('isDark 可写：赋值后落盘为 light/dark 模式', () => {
    const store = useAppStore()
    store.isDark = true
    expect(store.themeMode).toBe('dark')
    expect(localStorage.getItem('super_admin_theme_mode')).toBe('dark')
    store.isDark = false
    expect(store.themeMode).toBe('light')
  })

  it('setPrimary 更新主题色并持久化', () => {
    const store = useAppStore()
    store.setPrimary('#ff0000')
    expect(store.primary).toBe('#ff0000')
    expect(localStorage.getItem('super_admin_primary')).toBe('#ff0000')
  })

  it('toggleSidebar 切换侧边栏折叠并持久化', () => {
    const store = useAppStore()
    const initial = store.sidebarCollapsed
    store.toggleSidebar()
    expect(store.sidebarCollapsed).toBe(!initial)
    expect(localStorage.getItem('super_admin_sidebar_collapsed')).toBe(String(!initial))
  })
})
