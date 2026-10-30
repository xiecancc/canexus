/**
 * 用户管理页面挂载测试：mock 请求层与超管身份，
 * 验证部门树 / 用户表格渲染、搜索参数透传与 admin 行禁用。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { PagedResult } from '@/api/types'
import type { DeptVO } from '@/api/system/dept'
import type { UserVO } from '@/api/system/user'
import permission from '@/directives/permission'
import { useAuthStore } from '@/stores/auth'
import UserIndex from '../index.vue'

vi.mock('@/utils/request', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

import request from '@/utils/request'

const mockedGet = request.get as unknown as Mock

// jsdom 缺少 ResizeObserver，el-table / el-scrollbar 需要
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
window.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver

/** 空 GUID（后端树根父 ID 约定） */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** 构造用户行数据 */
function makeUser(partial: Partial<UserVO>): UserVO {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    userName: 'zhangsan',
    nickName: '张三',
    deptId: '00000000-0000-0000-0000-000000000100',
    deptName: '研发部',
    email: '',
    phone: '13800000000',
    status: 1,
    createdAt: '2026-01-01 10:00:00',
    roles: [{ id: '00000000-0000-0000-0000-000000000002', roleName: '普通角色' }],
    ...partial,
  }
}

const users: UserVO[] = [
  makeUser({}),
  makeUser({ id: '00000000-0000-0000-0000-000000000002', userName: 'admin', nickName: '超级管理员', roles: [] }),
]

const deptTree: DeptVO[] = [
  {
    id: '00000000-0000-0000-0000-000000000100',
    parentId: EMPTY_GUID,
    deptName: '研发部',
    orderNum: 1,
    leader: '',
    phone: '',
    email: '',
    status: 1,
    createdAt: '',
    children: [],
  },
]

/** 挂载用户管理页面 */
function mountUserPage() {
  return mount(UserIndex, {
    global: {
      directives: { permission },
    },
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  // 以超管身份挂载，v-permission 全部通过
  const authStore = useAuthStore()
  authStore.userInfo = {
    userId: '00000000-0000-0000-0000-000000000001',
    userName: 'admin',
    nickName: '管理员',
    avatar: '',
    roles: ['superadmin'],
    permissions: ['*:*:*'],
  }
  mockedGet.mockReset()
  mockedGet.mockImplementation((url: string) => {
    if (url === '/system/user/page') {
      return Promise.resolve<PagedResult<UserVO>>({
        items: users,
        total: 2,
        pageIndex: 1,
        pageSize: 10,
      })
    }
    if (url === '/system/dept/tree') {
      return Promise.resolve(deptTree)
    }
    return Promise.resolve(null)
  })
})

describe('用户管理页面', () => {
  it('挂载后渲染部门树与用户列表（含角色标签与时间格式化）', async () => {
    const wrapper = mountUserPage()
    await flushPromises()

    expect(wrapper.text()).toContain('研发部')
    expect(wrapper.text()).toContain('zhangsan')
    expect(wrapper.text()).toContain('普通角色')
    expect(wrapper.text()).toContain('2026-01-01 10:00:00')
  })

  it('输入关键字搜索时按新参数请求分页接口', async () => {
    const wrapper = mountUserPage()
    await flushPromises()
    mockedGet.mockClear()

    await wrapper.find('input[placeholder="用户名 / 昵称 / 手机号"]').setValue('zhang')
    const searchButton = wrapper.findAll('button').find((button) => button.text().includes('搜索'))
    await searchButton?.trigger('click')
    await flushPromises()

    expect(mockedGet).toHaveBeenCalledTimes(1)
    const [url, config] = mockedGet.mock.calls[0] as [string, { params: Record<string, unknown> }]
    expect(url).toBe('/system/user/page')
    expect(config.params.keyword).toBe('zhang')
  })

  it('超管 admin 行的状态开关禁用（普通行可用）', async () => {
    const wrapper = mountUserPage()
    await flushPromises()

    const rows = wrapper.findAll('.el-table__row')
    expect(rows.length).toBe(2)

    const normalSwitch = rows[0]?.find('input.el-switch__input')
    const adminRow = rows.find((row) => row.text().includes('admin'))
    const adminSwitch = adminRow?.find('input.el-switch__input')

    expect(normalSwitch?.attributes('disabled')).toBeUndefined()
    expect(adminSwitch?.attributes('disabled')).toBeDefined()
  })
})
