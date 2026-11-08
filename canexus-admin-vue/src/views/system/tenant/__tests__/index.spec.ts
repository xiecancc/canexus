/**
 * 租户管理页面挂载测试：mock 请求层与超管身份，
 * 验证租户表格渲染与搜索参数透传。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { PagedResult } from '@/api/types'
import type { TenantVO } from '@/api/system/tenant'
import permission from '@/directives/permission'
import { useAuthStore } from '@/stores/auth'
import TenantIndex from '../index.vue'

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

/** 构造租户行数据 */
function makeTenant(partial: Partial<TenantVO>): TenantVO {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    version: '00000000-0000-0000-0000-000000000001',
    tenantKey: 'acme',
    tenantName: 'Acme 公司',
    contactPhone: '13800000000',
    status: 1,
    description: '',
    expireTime: null,
    createdAt: '2026-01-01 10:00:00',
    ...partial,
  }
}

const tenants: TenantVO[] = [
  makeTenant({}),
  makeTenant({
    id: '00000000-0000-0000-0000-000000000002',
    tenantKey: 'beta',
    tenantName: 'Beta 公司',
    status: 0,
    contactPhone: '',
  }),
]

/** 挂载租户管理页面 */
function mountTenantPage() {
  return mount(TenantIndex, {
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
    if (url === '/system/tenant/page') {
      return Promise.resolve<PagedResult<TenantVO>>({
        items: tenants,
        total: 2,
        pageIndex: 1,
        pageSize: 10,
      })
    }
    return Promise.resolve(null)
  })
})

describe('租户管理页面', () => {
  it('挂载后渲染租户列表（标识 / 名称 / 时间格式化）', async () => {
    const wrapper = mountTenantPage()
    await flushPromises()

    expect(wrapper.text()).toContain('acme')
    expect(wrapper.text()).toContain('Acme 公司')
    expect(wrapper.text()).toContain('beta')
    expect(wrapper.text()).toContain('2026-01-01 10:00:00')
  })

  it('输入关键字搜索时按新参数请求分页接口', async () => {
    const wrapper = mountTenantPage()
    await flushPromises()
    mockedGet.mockClear()

    await wrapper.find('input[placeholder="租户标识 / 租户名称"]').setValue('acme')
    const searchButton = wrapper.findAll('button').find((button) => button.text().includes('搜索'))
    await searchButton?.trigger('click')
    await flushPromises()

    expect(mockedGet).toHaveBeenCalledTimes(1)
    const [url, config] = mockedGet.mock.calls[0] as [string, { params: Record<string, unknown> }]
    expect(url).toBe('/system/tenant/page')
    expect(config.params.keyword).toBe('acme')
  })

  it('渲染新增与操作按钮（超管权限全通过）', async () => {
    const wrapper = mountTenantPage()
    await flushPromises()

    const buttonTexts = wrapper.findAll('button').map((button) => button.text())
    expect(buttonTexts.some((text) => text.includes('新增'))).toBe(true)
    expect(buttonTexts.some((text) => text.includes('编辑'))).toBe(true)
    expect(buttonTexts.some((text) => text.includes('删除'))).toBe(true)
  })
})
