/**
 * 用户模块接口测试：mock 请求层，验证接口路径、HTTP 方法与参数透传。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import type { PagedResult } from '@/api/types'

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
  createUserApi,
  deleteUserApi,
  getUserApi,
  getUserPageApi,
  importUserApi,
  updateUserApi,
  updateUserPasswordApi,
  updateUserRolesApi,
  updateUserStatusApi,
} from '../user'

const mockedGet = request.get as unknown as Mock
const mockedPost = request.post as unknown as Mock
const mockedPut = request.put as unknown as Mock
const mockedDelete = request.delete as unknown as Mock

/** 测试用用户 ID（GUID 字符串） */
const USER_ID = '00000000-0000-0000-0000-000000000009'

/** 测试用部门 ID（GUID 字符串） */
const DEPT_ID = '00000000-0000-0000-0000-000000000100'

/** 测试用角色 ID（GUID 字符串） */
const ROLE_ID_1 = '00000000-0000-0000-0000-000000000001'
const ROLE_ID_2 = '00000000-0000-0000-0000-000000000002'

describe('用户模块接口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('分页查询：GET /system/user/page 且查询参数透传', async () => {
    const paged: PagedResult<unknown> = { items: [], total: 0, pageIndex: 1, pageSize: 10 }
    mockedGet.mockResolvedValueOnce(paged)
    const params = { keyword: '张', deptId: DEPT_ID, status: 1, pageIndex: 2, pageSize: 20 }
    await expect(getUserPageApi(params)).resolves.toBe(paged)
    expect(mockedGet).toHaveBeenCalledWith('/system/user/page', { params })
  })

  it('创建用户：POST /system/user 且请求体透传', async () => {
    const body = {
      userName: 'zhangsan',
      nickName: '张三',
      password: '123456',
      deptId: DEPT_ID,
      status: 1,
      roleIds: [ROLE_ID_1, ROLE_ID_2],
    }
    await createUserApi(body)
    expect(mockedPost).toHaveBeenCalledWith('/system/user', body)
  })

  it('更新用户：PUT /system/user/{id} 且请求体不含账号密码', async () => {
    const body = {
      nickName: '李四',
      deptId: DEPT_ID,
      email: '',
      phone: '',
      status: 1,
      description: '',
      roleIds: [],
    }
    await updateUserApi(USER_ID, body)
    expect(mockedPut).toHaveBeenCalledWith(`/system/user/${USER_ID}`, body)
  })

  it('删除用户：DELETE /system/user/{id}', async () => {
    await deleteUserApi(USER_ID)
    expect(mockedDelete).toHaveBeenCalledWith(`/system/user/${USER_ID}`)
  })

  it('修改状态：PUT /system/user/{id}/status 且请求体为 { status }', async () => {
    await updateUserStatusApi(USER_ID, 0)
    expect(mockedPut).toHaveBeenCalledWith(`/system/user/${USER_ID}/status`, { status: 0 })
  })

  it('重置密码：PUT /system/user/{id}/password 且请求体为 { password }', async () => {
    await updateUserPasswordApi(USER_ID, 'abc12345')
    expect(mockedPut).toHaveBeenCalledWith(`/system/user/${USER_ID}/password`, {
      password: 'abc12345',
    })
  })

  it('分配角色：PUT /system/user/{id}/roles 且请求体为 { roleIds }', async () => {
    await updateUserRolesApi(USER_ID, [ROLE_ID_1, ROLE_ID_2])
    expect(mockedPut).toHaveBeenCalledWith(`/system/user/${USER_ID}/roles`, {
      roleIds: [ROLE_ID_1, ROLE_ID_2],
    })
  })

  it('用户详情：GET /system/user/{id}', async () => {
    mockedGet.mockResolvedValueOnce({ id: USER_ID, roleIds: [] })
    await expect(getUserApi(USER_ID)).resolves.toEqual({ id: USER_ID, roleIds: [] })
    expect(mockedGet).toHaveBeenCalledWith(`/system/user/${USER_ID}`)
  })

  it('导入用户：POST /system/user/import 且 FormData 原样透传（file 字段）', async () => {
    const result = { successCount: 3, failureRows: [{ row: 5, reason: '手机号格式错误' }] }
    mockedPost.mockResolvedValueOnce(result)
    const formData = new FormData()
    formData.append('file', new File(['demo'], 'users.xlsx'))
    await expect(importUserApi(formData)).resolves.toBe(result)
    expect(mockedPost).toHaveBeenCalledTimes(1)
    const [url, body] = mockedPost.mock.calls[0] as [string, FormData]
    expect(url).toBe('/system/user/import')
    expect(body).toBe(formData)
    expect(body.get('file')).toBeInstanceOf(File)
  })
})
