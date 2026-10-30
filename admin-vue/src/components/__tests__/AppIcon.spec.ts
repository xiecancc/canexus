import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import AppIcon from '../AppIcon.vue'

describe('AppIcon 图标组件', () => {
  it('按名称渲染对应图标', () => {
    const wrapper = mount(AppIcon, { props: { name: 'Search' } })
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.findComponent({ name: 'Search' }).exists()).toBe(true)
  })

  it('未匹配名称时渲染占位图标（QuestionFilled）', () => {
    const wrapper = mount(AppIcon, { props: { name: 'NotExistsIcon' } })
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.findComponent({ name: 'QuestionFilled' }).exists()).toBe(true)
  })

  it('未传名称时渲染占位图标', () => {
    const wrapper = mount(AppIcon)
    expect(wrapper.findComponent({ name: 'QuestionFilled' }).exists()).toBe(true)
  })
})
