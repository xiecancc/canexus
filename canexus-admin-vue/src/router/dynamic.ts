/**
 * 动态路由转换：将后端 RouteVO[] 转换为 vue-router 路由记录。
 *
 * 约定：
 * - 组件路径映射到 src/views 下的 index.vue（如 system/user → /src/views/system/user/index.vue）
 * - component 为空或 Layout 表示目录占位，不设置组件（由子路由渲染）
 * - 无 component 且无子级视为按钮类型，不生成路由
 * - 映射不到的组件回落到建设中占位页并 console.warn
 * - name 缺失时按 path 生成大驼峰路由名
 */
import type { Component } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import type { RouteVO } from '@/api/auth'

/** 视图组件集合（key 为 /src/views/.../index.vue 形式的文件路径） */
const viewModules = import.meta.glob('/src/views/**/index.vue') as Record<
  string,
  () => Promise<Component>
>

/** 目录占位组件标识（后端顶级目录 component 惯用值） */
const LAYOUT_COMPONENT = 'Layout'

/** 建设中占位页（组件映射失败时的回落页面） */
const UNDER_CONSTRUCTION_KEY = '/src/views/under-construction/index.vue'

/** 按 path 生成路由名：/system/user → SystemUser */
function buildRouteName(path: string): string {
  const name = path
    .split('/')
    .filter(Boolean)
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1))
    .join('')
  // 兜底：path 为空等极端情况（正常不会发生）
  return name || `Route${Date.now()}`
}

/** 是否按钮类型路由（无 component 且无子级，不生成路由） */
function isButtonRoute(vo: RouteVO): boolean {
  return !vo.component && !vo.children?.length
}

/** 解析组件路径为异步组件加载器 */
function resolveViewComponent(
  component: string | undefined,
): (() => Promise<Component>) | undefined {
  // 目录占位：不设置组件，命中子路由渲染
  if (!component || component === LAYOUT_COMPONENT) {
    return undefined
  }
  const normalized = component.replace(/^\//, '').replace(/\/index$/, '')
  const key = `/src/views/${normalized}/index.vue`
  const loader = viewModules[key]
  if (!loader) {
    console.warn(`[router] 未找到组件 ${component}（${key}），已回落到建设中占位页`)
    return viewModules[UNDER_CONSTRUCTION_KEY]
  }
  return loader
}

/** 按 meta.order 升序排序（未设置时排最后，保持原有相对顺序） */
function sortByOrder(records: RouteRecordRaw[]): RouteRecordRaw[] {
  return [...records].sort(
    (a, b) =>
      (a.meta?.order ?? Number.POSITIVE_INFINITY) - (b.meta?.order ?? Number.POSITIVE_INFINITY),
  )
}

/**
 * 将后端 RouteVO[] 递归转换为 vue-router 路由记录（含按钮过滤与排序）。
 */
export function transformRoutes(vos: RouteVO[]): RouteRecordRaw[] {
  const records = vos
    .filter((vo) => !isButtonRoute(vo))
    .map<RouteRecordRaw>((vo) => {
      // RouteRecordRaw 为受限联合类型，先以基础字段断言，再按需补充 component / children
      const record = {
        name: vo.name || buildRouteName(vo.path),
        path: vo.path,
        redirect: vo.redirect,
        // 元信息整体透传（title / icon / keepAlive / hidden / order / perms）
        meta: { ...vo.meta },
      } as RouteRecordRaw
      const component = resolveViewComponent(vo.component)
      if (component) {
        record.component = component
      }
      if (vo.children?.length) {
        const children = transformRoutes(vo.children)
        if (children.length) {
          record.children = children
        }
      }
      return record
    })
  return sortByOrder(records)
}
