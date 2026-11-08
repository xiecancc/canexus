/**
 * 菜单模块接口与纯逻辑测试：
 * - 接口路径、HTTP 方法与参数透传（mock 请求层）
 * - getCheckedMenuIds：角色授权菜单 ID 全选集合 → 回显勾选集合（仅叶子）
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'

vi.mock('@/utils/request', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

import request from '@/utils/request'
import type { MenuVO } from '../menu'
import {
  collectMenuIds,
  createMenuApi,
  deleteMenuApi,
  getCheckedMenuIds,
  getMenuApi,
  getMenuTreeApi,
  updateMenuApi,
} from '../menu'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 空 GUID（后端树根父 ID 约定） */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** 由数字构造测试用 GUID 字符串（00000000-0000-0000-0000-0000000000NN） */
const gid = (n: number): string => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`

/** 构造菜单树节点 */
function makeMenu(partial: Partial<MenuVO> & Pick<MenuVO, 'id'>): MenuVO {
  return {
    version: '00000000-0000-0000-0000-000000000001',
    parentId: EMPTY_GUID,
    menuType: 1,
    menuName: '',
    path: '',
    component: '',
    perms: '',
    icon: '',
    orderNum: 0,
    visible: 1,
    keepAlive: false,
    status: 1,
    createdAt: '',
    children: [],
    ...partial,
  }
}

/** 测试树：1 系统管理(2 用户管理(21/22 按钮), 3 角色管理) / 4 系统工具(5 构建) */
const tree: MenuVO[] = [
  makeMenu({
    id: gid(1),
    menuName: '系统管理',
    children: [
      makeMenu({
        id: gid(2),
        menuType: 2,
        menuName: '用户管理',
        parentId: gid(1),
        children: [
          makeMenu({ id: gid(21), menuType: 3, menuName: '用户新增', parentId: gid(2) }),
          makeMenu({ id: gid(22), menuType: 3, menuName: '用户删除', parentId: gid(2) }),
        ],
      }),
      makeMenu({ id: gid(3), menuType: 2, menuName: '角色管理', parentId: gid(1) }),
    ],
  }),
  makeMenu({
    id: gid(4),
    menuName: '系统工具',
    children: [makeMenu({ id: gid(5), menuType: 2, menuName: '构建', parentId: gid(4) })],
  }),
]

describe('菜单模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('菜单树查询：GET /system/menu/tree 且查询参数透传', async () => {
    await getMenuTreeApi({ keyword: '用户', status: 1 })
    expect(mockedGet).toHaveBeenCalledWith('/system/menu/tree', {
      params: { keyword: '用户', status: 1 },
    })
  })

  it('菜单详情：GET /system/menu/{id}', async () => {
    await getMenuApi(gid(7))
    expect(mockedGet).toHaveBeenCalledWith(`/system/menu/${gid(7)}`)
  })

  it('创建菜单：POST /system/menu 且请求体透传', async () => {
    const body = {
      parentId: EMPTY_GUID,
      menuType: 2 as const,
      menuName: '用户管理',
      path: 'user',
      component: 'system/user/index',
      perms: undefined,
      icon: 'User',
      orderNum: 1,
      visible: 1,
      keepAlive: true,
      status: 1,
    }
    await createMenuApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/menu', body)
  })

  it('更新菜单：PUT /system/menu/{id}/{version}', async () => {
    const body = {
      parentId: EMPTY_GUID,
      menuType: 3 as const,
      menuName: '用户新增',
      path: '',
      perms: 'system:user:add',
      orderNum: 0,
      visible: 1,
      keepAlive: false,
      status: 1,
    }
    await updateMenuApi(gid(21), gid(99), body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/menu/${gid(21)}/${gid(99)}`, body)
  })

  it('删除菜单：DELETE /system/menu/{id}/{version}', async () => {
    await deleteMenuApi(gid(21), gid(99))
    expect(mockedDelete).toHaveBeenCalledWith(`/system/menu/${gid(21)}/${gid(99)}`)
  })
})

describe('getCheckedMenuIds 回显勾选集合', () => {
  it('全选集合（含父与子）：仅返回叶子节点', () => {
    const allIds = [gid(1), gid(2), gid(21), gid(22), gid(3), gid(4), gid(5)]
    expect(getCheckedMenuIds(tree, allIds)).toEqual([gid(21), gid(22), gid(3), gid(5)])
  })

  it('部分选中：仅返回选中的叶子，半选父级交给树联动计算', () => {
    expect(getCheckedMenuIds(tree, [gid(1), gid(2), gid(21)])).toEqual([gid(21)])
  })

  it('空集合返回空数组', () => {
    expect(getCheckedMenuIds(tree, [])).toEqual([])
  })

  it('空目录（无子节点）被选中时按叶子返回', () => {
    const withEmptyDir: MenuVO[] = [
      ...tree,
      makeMenu({ id: gid(9), menuName: '空目录', children: [] }),
    ]
    expect(getCheckedMenuIds(withEmptyDir, [gid(9)])).toEqual([gid(9)])
  })

  it('父级选中但无子级选中（病态数据兜底）：直接返回父级自身', () => {
    expect(getCheckedMenuIds(tree, [gid(1)])).toEqual([gid(1)])
  })

  it('children 缺失的节点视为叶子', () => {
    const brokenTree = [makeMenu({ id: gid(6), menuName: '无子字段节点', children: undefined })]
    expect(getCheckedMenuIds(brokenTree, [gid(6)])).toEqual([gid(6)])
  })
})

describe('collectMenuIds 收集节点', () => {
  it('收集树中全部节点 ID（含父与子）', () => {
    expect(collectMenuIds(tree)).toEqual([gid(1), gid(2), gid(21), gid(22), gid(3), gid(4), gid(5)])
  })
})
