/**
 * DictTag 字典标签组件测试：
 * 验证动态字典 dictType 拉取渲染、tagType 数字 1-5 映射、静态字典项渲染与未匹配回退。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

const mocks = vi.hoisted(() => ({
  getDictDataByTypeApi: vi.fn(),
}))

vi.mock('@/api/system/dict', () => ({
  getDictDataByTypeApi: mocks.getDictDataByTypeApi,
}))

import DictTag from '../DictTag.vue'
import type { DictDataVO } from '@/api/system/dict'
import { clearDictCache } from '@/composables/useDict'
import { SYS_NORMAL_DISABLE } from '@/utils/dict'

/** 构造动态字典数据项 */
function makeItem(value: string, tagType: number): DictDataVO {
  return {
    id: `00000000-0000-0000-0000-${value}`,
    version: '00000000-0000-0000-0000-000000000001',
    dictType: 'sys_oper_type',
    dictLabel: `标签${value}`,
    dictValue: value,
    tagType,
    description: '',
    orderNum: 1,
    status: 1,
    createdAt: '',
  }
}

beforeEach(() => {
  clearDictCache()
  mocks.getDictDataByTypeApi.mockReset()
})

/** 挂载 DictTag */
function mountTag(props: InstanceType<typeof DictTag>['$props']) {
  return mount(DictTag, { props })
}

describe('DictTag 字典标签', () => {
  it('dictType 模式：经 useDict 拉取字典并按 tagType 渲染标签', async () => {
    mocks.getDictDataByTypeApi.mockResolvedValueOnce([makeItem('2', 2)])
    const wrapper = mountTag({ dictType: 'sys_oper_type', value: '2' })
    await flushPromises()

    const tag = wrapper.find('.el-tag')
    expect(tag.exists()).toBe(true)
    expect(tag.text()).toBe('标签2')
    expect(tag.classes()).toContain('el-tag--success')
    expect(mocks.getDictDataByTypeApi).toHaveBeenCalledWith('sys_oper_type')
  })

  it.each([
    [1, 'el-tag--primary'],
    [2, 'el-tag--success'],
    [3, 'el-tag--info'],
    [4, 'el-tag--warning'],
    [5, 'el-tag--danger'],
  ])('tagType=%s 映射到 %s', async (tagType, expectedClass) => {
    const item = makeItem(String(tagType), tagType as number)
    const wrapper = mountTag({ items: [item], value: String(tagType) })
    await flushPromises()
    expect(wrapper.find('.el-tag').classes()).toContain(expectedClass)
  })

  it('items 模式支持静态字典项（utils/dict）渲染', () => {
    const wrapper = mountTag({ items: SYS_NORMAL_DISABLE, value: 0 })
    const tag = wrapper.find('.el-tag')
    expect(tag.text()).toBe('停用')
    expect(tag.classes()).toContain('el-tag--danger')
  })

  it('未匹配到字典项时显示原值并回退 info 色', () => {
    const wrapper = mountTag({ items: [makeItem('1', 1)], value: '99' })
    const tag = wrapper.find('.el-tag')
    expect(tag.text()).toBe('99')
    expect(tag.classes()).toContain('el-tag--info')
  })

  it('空值显示占位符且不渲染标签', () => {
    const wrapper = mountTag({ dictType: 'sys_oper_type', value: null })
    expect(wrapper.find('.el-tag').exists()).toBe(false)
    expect(wrapper.text()).toBe('--')
  })
})
