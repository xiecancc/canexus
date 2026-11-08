/**
 * useDict 动态字典组合式函数测试：
 * 验证模块级缓存、同类型并发共享请求、refresh 强制刷新与失败重试。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'

const mocks = vi.hoisted(() => ({
  getDictDataByTypeApi: vi.fn(),
}))

vi.mock('@/api/system/dict', () => ({
  getDictDataByTypeApi: mocks.getDictDataByTypeApi,
}))

import { clearDictCache, useDict } from '../useDict'
import type { DictDataVO } from '@/api/system/dict'

/** 构造字典数据项 */
function makeItem(value: string): DictDataVO {
  return {
    id: `00000000-0000-0000-0000-${value}`,
    version: '00000000-0000-0000-0000-000000000001',
    dictType: 'sys_test',
    dictLabel: `标签${value}`,
    dictValue: value,
    tagType: 1,
    description: '',
    orderNum: 1,
    status: 1,
    createdAt: '2026-01-01 00:00:00',
  }
}

beforeEach(() => {
  clearDictCache()
  mocks.getDictDataByTypeApi.mockReset()
})

describe('useDict 动态字典', () => {
  it('首次调用立即拉取并写入模块级缓存', async () => {
    mocks.getDictDataByTypeApi.mockResolvedValueOnce([makeItem('1')])
    const { items } = useDict('sys_oper_type')

    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledWith('sys_oper_type')
    await flushPromises()
    expect(items.value).toEqual([makeItem('1')])

    // 模块级缓存生效：同类型再次调用不再发请求且共享同一份数据
    const again = useDict('sys_oper_type')
    await flushPromises()
    expect(again.items).toBe(items)
    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledTimes(1)
  })

  it('同类型并发调用共享同一次请求与同一份数据', async () => {
    let resolveApi: (value: DictDataVO[]) => void = () => {}
    mocks.getDictDataByTypeApi.mockImplementationOnce(
      () => new Promise<DictDataVO[]>((resolve) => (resolveApi = resolve)),
    )

    const first = useDict('sys_common_status')
    const second = useDict('sys_common_status')

    expect(first.items).toBe(second.items)
    resolveApi([makeItem('1'), makeItem('2')])
    await flushPromises()

    expect(first.items.value).toHaveLength(2)
    expect(second.items.value).toHaveLength(2)
    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledTimes(1)
  })

  it('refresh 强制重新拉取并更新缓存', async () => {
    mocks.getDictDataByTypeApi.mockResolvedValueOnce([makeItem('1')])
    const { items, refresh } = useDict('sys_show_hide')
    await flushPromises()
    expect(items.value).toHaveLength(1)

    mocks.getDictDataByTypeApi.mockResolvedValueOnce([makeItem('1'), makeItem('3')])
    await refresh()
    expect(items.value).toHaveLength(2)
    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledTimes(2)
  })

  it('refresh 在请求进行中调用时共享进行中的请求', async () => {
    let resolveApi: (value: DictDataVO[]) => void = () => {}
    mocks.getDictDataByTypeApi.mockImplementationOnce(
      () => new Promise<DictDataVO[]>((resolve) => (resolveApi = resolve)),
    )

    const { items, refresh } = useDict('sys_share')
    const refreshPromise = refresh()
    resolveApi([makeItem('1'), makeItem('2')])
    await refreshPromise

    expect(items.value).toHaveLength(2)
    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledTimes(1)
  })

  it('拉取失败不写入脏数据，重新调用可重试', async () => {
    mocks.getDictDataByTypeApi.mockRejectedValueOnce(new Error('网络异常'))
    const first = useDict('sys_fail')
    await flushPromises()
    expect(first.items.value).toEqual([])

    mocks.getDictDataByTypeApi.mockResolvedValueOnce([makeItem('1')])
    const second = useDict('sys_fail')
    await flushPromises()
    expect(second.items).toBe(first.items)
    expect(second.items.value).toEqual([makeItem('1')])
  })
})
