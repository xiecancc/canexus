/**
 * 角色模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
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
import {
  createRoleApi,
  deleteRoleApi,
  getAllRolesApi,
  getRoleApi,
  getRolePageApi,
  updateRoleApi,
} from '../role'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用角色 ID（GUID 字符串） */
const ROLE_ID = '00000000-0000-0000-0000-000000000003'

/** 测试用菜单 ID（GUID 字符串） */
const MENU_ID_1 = '00000000-0000-0000-0000-000000000001'
const MENU_ID_2 = '00000000-0000-0000-0000-000000000002'

describe('角色模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /system/role/page 且查询参数透传', async () => {
    const params = { keyword: 'admin', status: 1, pageIndex: 1, pageSize: 10 }
    await getRolePageApi(params)
    expect(mockedGet).toHaveBeenCalledWith('/system/role/page', { params })
  })

  it('查询全部角色：GET /system/role/all', async () => {
    const roles = [{ id: ROLE_ID, roleName: '管理员', roleKey: 'admin', status: 1 }]
    mockedGet.mockResolvedValueOnce(roles)
    await expect(getAllRolesApi()).resolves.toBe(roles)
    expect(mockedGet).toHaveBeenCalledWith('/system/role/all')
  })

  it('角色详情：GET /system/role/{id}', async () => {
    await getRoleApi(ROLE_ID)
    expect(mockedGet).toHaveBeenCalledWith(`/system/role/${ROLE_ID}`)
  })

  it('创建角色：POST /system/role 且请求体含 menuIds', async () => {
    const body = {
      roleName: '访客',
      roleKey: 'guest',
      dataScope: 5,
      status: 1,
      description: '',
      menuIds: [MENU_ID_1, MENU_ID_2],
    }
    await createRoleApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/role', body)
  })

  it('更新角色：PUT /system/role/{id}（用于状态整体更新等场景）', async () => {
    const body = {
      roleName: '访客',
      roleKey: 'guest',
      dataScope: 5,
      status: 0,
      description: '',
      menuIds: [MENU_ID_1],
    }
    await updateRoleApi(ROLE_ID, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/role/${ROLE_ID}`, body)
  })

  it('删除角色：DELETE /system/role/{id}', async () => {
    await deleteRoleApi(ROLE_ID)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/role/${ROLE_ID}`)
  })
})
