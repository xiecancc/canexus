/**
 * v-permission 按钮级权限指令。
 *
 * 用法：v-permission="'system:user:add'" 或 v-permission="['a:b:c', 'd:e:f']"（任一匹配即通过）；
 * 校验不通过时移除元素（后端权限数据驱动，无权限标识直接隐藏入口）。
 */
import type { Directive, DirectiveBinding } from 'vue'
import { usePermission } from '@/composables/usePermission'

const permission: Directive<HTMLElement, string | string[]> = {
  mounted(el: HTMLElement, binding: DirectiveBinding<string | string[]>) {
    const { hasPerm } = usePermission()
    if (!hasPerm(binding.value)) {
      el.parentNode?.removeChild(el)
    }
  },
}

export default permission
