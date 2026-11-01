/**
 * 通知公告页面挂载测试：mock 请求层与超管身份，
 * 验证公告表格渲染（标题 / 本地类型常量标签）与搜索参数透传。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { PagedResult } from '@/api/types'
import type { NoticeVO } from '@/api/system/notice'
import permission from '@/directives/permission'
import { useAuthStore } from '@/stores/auth'
import NoticeIndex from '../index.vue'

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

/** 构造公告行数据 */
function makeNotice(partial: Partial<NoticeVO>): NoticeVO {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    title: '系统升级公告',
    noticeType: 2,
    content: '今晚 23:00 升级',
    status: 1,
    description: '',
    createdAt: '2026-01-01 10:00:00',
    ...partial,
  }
}

const notices: NoticeVO[] = [
  makeNotice({}),
  makeNotice({
    id: '00000000-0000-0000-0000-000000000002',
    title: '节假日值班通知',
    noticeType: 1,
    status: 0,
  }),
]

/** 挂载通知公告页面 */
function mountNoticePage() {
  return mount(NoticeIndex, {
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
    if (url === '/system/notice/page') {
      return Promise.resolve<PagedResult<NoticeVO>>({
        items: notices,
        total: 2,
        pageIndex: 1,
        pageSize: 10,
      })
    }
    return Promise.resolve(null)
  })
})

describe('通知公告页面', () => {
  it('挂载后渲染公告列表（标题 / 类型标签 / 创建时间）', async () => {
    const wrapper = mountNoticePage()
    await flushPromises()

    expect(wrapper.text()).toContain('系统升级公告')
    expect(wrapper.text()).toContain('节假日值班通知')
    expect(wrapper.text()).toContain('公告')
    expect(wrapper.text()).toContain('通知')
    expect(wrapper.text()).toContain('2026-01-01 10:00:00')
  })

  it('输入标题搜索时按新参数请求分页接口', async () => {
    const wrapper = mountNoticePage()
    await flushPromises()
    mockedGet.mockClear()

    await wrapper.find('input[placeholder="请输入公告标题"]').setValue('升级')
    const searchButton = wrapper.findAll('button').find((button) => button.text().includes('搜索'))
    await searchButton?.trigger('click')
    await flushPromises()

    expect(mockedGet).toHaveBeenCalledTimes(1)
    const [url, config] = mockedGet.mock.calls[0] as [string, { params: Record<string, unknown> }]
    expect(url).toBe('/system/notice/page')
    expect(config.params.title).toBe('升级')
  })
})
