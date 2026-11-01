/**
 * 系统菜单模块接口、后端契约类型与纯逻辑辅助函数。
 *
 * 契约约定（后端并行开发，以此为准）：
 * - GET    /system/menu/tree?keyword=&status= → MenuVO[]（树结构）
 * - GET    /system/menu/{id}                  → MenuVO
 * - POST   /system/menu                       → 创建菜单
 * - PUT    /system/menu/{id}                  → 更新菜单
 * - DELETE /system/menu/{id}                  → 删除菜单
 * - 菜单类型：1 目录 2 菜单 3 按钮；可见性：1 显示 0 隐藏；状态：1 正常 0 停用；顶级菜单 parentId 为空 GUID
 */
import request from '@/utils/request'

/** 菜单类型：1 目录 2 菜单 3 按钮 */
export type MenuType = 1 | 2 | 3

/** 菜单树节点 */
export interface MenuVO {
  /** 菜单 ID（GUID 字符串） */
  id: string
  /** 父菜单 ID（顶级为空 GUID：00000000-0000-0000-0000-000000000000） */
  parentId: string
  /** 菜单类型：1 目录 2 菜单 3 按钮 */
  menuType: MenuType
  /** 菜单名称 */
  menuName: string
  /** 路由地址 */
  path: string
  /** 组件路径（相对 src/views，如 system/user/index；仅菜单类型） */
  component: string
  /** 权限标识（如 system:user:list） */
  perms: string
  /** 图标名称（@element-plus/icons-vue 导出名） */
  icon: string
  /** 显示排序（越小越靠前） */
  orderNum: number
  /** 是否显示：1 显示 0 隐藏 */
  visible: number
  /** 是否缓存页面 */
  keepAlive: boolean
  /** 状态：1 正常 0 停用 */
  status: number
  /** 创建时间 */
  createdAt: string
  /** 子菜单 */
  children: MenuVO[]
}

/** 菜单树查询参数 */
export interface MenuTreeRequest {
  /** 关键字：匹配菜单名称 / 权限标识 */
  keyword?: string
  /** 状态：1 正常 0 停用 */
  status?: number
}

/** 创建 / 更新菜单请求（更新时 ID 位于路径） */
export interface MenuSaveRequest {
  /** 父菜单 ID（顶级为空 GUID：00000000-0000-0000-0000-000000000000） */
  parentId: string
  /** 菜单类型：1 目录 2 菜单 3 按钮 */
  menuType: MenuType
  /** 菜单名称 */
  menuName: string
  /** 路由地址 */
  path: string
  /** 组件路径（仅菜单类型） */
  component?: string
  /** 权限标识 */
  perms?: string
  /** 图标名称 */
  icon?: string
  /** 显示排序 */
  orderNum: number
  /** 是否显示：1 显示 0 隐藏 */
  visible: number
  /** 是否缓存页面 */
  keepAlive: boolean
  /** 状态：1 正常 0 停用 */
  status: number
}

/** 查询菜单树 */
export function getMenuTreeApi(params?: MenuTreeRequest): Promise<MenuVO[]> {
  return request.get<MenuVO[]>('/system/menu/tree', { params })
}

/** 查询菜单详情 */
export function getMenuApi(id: string): Promise<MenuVO> {
  return request.get<MenuVO>(`/system/menu/${id}`)
}

/** 创建菜单 */
export function createMenuApi(data: MenuSaveRequest): Promise<null> {
  return request.post<null>('/system/menu', data)
}

/** 更新菜单 */
export function updateMenuApi(id: string, data: MenuSaveRequest): Promise<null> {
  return request.put<null>(`/system/menu/${id}`, data)
}

/** 删除菜单 */
export function deleteMenuApi(id: string): Promise<null> {
  return request.delete<null>(`/system/menu/${id}`)
}

/**
 * 从角色授权的菜单 ID 全选集合（含父与子）中提取应显式勾选的节点 ID。
 *
 * 编辑回显策略：仅对叶子节点调用 setChecked（不向下联动），父级由 el-tree
 * 父子联动自动计算——全部子级选中 → 父级自动全选，部分子级选中 → 父级呈半选，
 * 从而完整还原保存时的勾选状态。
 *
 * 判定为"应显式勾选"的节点：
 * - 树中无子节点（真叶子 / 空目录），且 ID 在集合中；
 * - 有子节点但没有任何子节点 ID 在集合中（病态数据兜底，直接勾选自身）。
 */
export function getCheckedMenuIds(tree: MenuVO[], menuIds: string[]): string[] {
  const idSet = new Set(menuIds)
  const result: string[] = []
  const walk = (nodes: MenuVO[]): void => {
    nodes.forEach((node) => {
      if (idSet.has(node.id)) {
        const hasSelectedChild = (node.children ?? []).some((child) => idSet.has(child.id))
        if (!node.children?.length || !hasSelectedChild) {
          result.push(node.id)
        }
      }
      if (node.children?.length) {
        walk(node.children)
      }
    })
  }
  walk(tree)
  return result
}

/** 收集菜单树全部节点 ID（含父与子），供测试与辅助场景使用 */
export function collectMenuIds(tree: MenuVO[]): string[] {
  const ids: string[] = []
  const walk = (nodes: MenuVO[]): void => {
    nodes.forEach((node) => {
      ids.push(node.id)
      if (node.children?.length) {
        walk(node.children)
      }
    })
  }
  walk(tree)
  return ids
}
