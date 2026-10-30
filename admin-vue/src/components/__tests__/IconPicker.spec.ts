/**
 * IconPicker 图标选择器组件测试：触发器展示、清空、网格分页、选中与搜索过滤。
 */
import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import IconPicker from '../IconPicker.vue'

// jsdom 缺少 ResizeObserver，部分 Element Plus 组件需要
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
window.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver

/** 以非传送模式挂载（弹层渲染在组件内部，便于查询断言） */
function mountPicker(props: Record<string, unknown> = {}) {
  return mount(IconPicker, { props: { teleported: false, ...props } })
}

/** 打开弹层面板 */
async function openPanel(wrapper: ReturnType<typeof mountPicker>): Promise<void> {
  await wrapper.find('.icon-picker-trigger').trigger('click')
  await nextTick()
  await nextTick()
}

describe('IconPicker 图标选择器', () => {
  it('未选择图标时展示占位文案', () => {
    const wrapper = mountPicker()
    expect(wrapper.find('.trigger-placeholder').text()).toBe('请选择图标')
  })

  it('已选择图标时展示图标名与清空按钮', () => {
    const wrapper = mountPicker({ modelValue: 'Search' })
    expect(wrapper.find('.trigger-name').text()).toBe('Search')
    expect(wrapper.find('.trigger-clear').exists()).toBe(true)
  })

  it('点击清空按钮回传空值', async () => {
    const wrapper = mountPicker({ modelValue: 'Search' })
    await wrapper.find('.trigger-clear').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([''])
  })

  it('弹层网格按每页 32 个图标分页展示', async () => {
    const wrapper = mountPicker()
    await openPanel(wrapper)
    expect(wrapper.findAll('.icon-cell').length).toBe(32)
  })

  it('点击图标回传选中名称', async () => {
    const wrapper = mountPicker()
    await openPanel(wrapper)
    const firstCell = wrapper.findAll('.icon-cell')[0]
    const name = firstCell?.attributes('title') ?? ''
    await firstCell?.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([name])
  })

  it('搜索关键字过滤图标网格', async () => {
    const wrapper = mountPicker()
    await openPanel(wrapper)
    await wrapper.find('.icon-picker-panel input').setValue('search')
    await nextTick()
    const cells = wrapper.findAll('.icon-cell')
    expect(cells.length).toBeGreaterThan(0)
    cells.forEach((cell) => {
      expect((cell.attributes('title') ?? '').toLowerCase()).toContain('search')
    })
  })
})
