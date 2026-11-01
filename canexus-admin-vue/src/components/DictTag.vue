<script setup lang="ts">
/**
 * 字典标签组件：将字典值渲染为对应标签色的 el-tag。
 *
 * - 传入 items：使用给定字典项集合（静态字典 utils/dict 或已拉取的动态字典均可）
 * - 传入 dictType：内部经 useDict 拉取并缓存（同类型全局共享，需保证 setup 时类型已确定）
 * - 未匹配到字典项时显示原值；值为空时显示占位符
 */
import { computed } from 'vue'
import { DICT_TAG_TYPE_MAP, useDict } from '@/composables/useDict'
import type { DictItem, TagTypeName } from '@/composables/useDict'
import type { DictItem as StaticDictItem } from '@/utils/dict'

const props = defineProps<{
  /** 字典项集合（与 dictType 二选一，优先使用） */
  items?: (StaticDictItem | DictItem)[]
  /** 动态字典类型（经 useDict 拉取并缓存） */
  dictType?: string
  /** 字典值 */
  value: string | number | null | undefined
}>()

// dictType 在组件 setup 时确定（页签场景类型固定），不支持动态变更后重新拉取
const dynamicState = props.dictType ? useDict(props.dictType) : undefined

/** 归一化后的字典项（屏蔽静态 / 动态两种形状差异） */
interface NormalizedItem {
  value: string
  label: string
  tagType: TagTypeName
}

/** 归一化单条字典项：动态项 tagType 为数字 1-5，静态项已是类型名 */
function normalizeItem(item: StaticDictItem | DictItem): NormalizedItem {
  if ('dictLabel' in item) {
    return {
      value: String(item.dictValue),
      label: item.dictLabel,
      tagType: DICT_TAG_TYPE_MAP[item.tagType] ?? 'info',
    }
  }
  return { value: item.value, label: item.label, tagType: item.tagType }
}

/** 参与匹配的字典项集合 */
const normalizedItems = computed<NormalizedItem[]>(() => {
  if (props.items?.length) {
    return props.items.map(normalizeItem)
  }
  return (dynamicState?.items.value ?? []).map(normalizeItem)
})

/** 值是否非空（空值渲染占位符而非原值） */
const hasValue = computed(
  () => props.value !== null && props.value !== undefined && props.value !== '',
)

/** 命中的字典项 */
const matched = computed(() =>
  hasValue.value
    ? normalizedItems.value.find((item) => item.value === String(props.value))
    : undefined,
)

/** 展示文本：命中取标签，未命中显示原值，空值显示占位符 */
const label = computed(() => {
  if (matched.value) {
    return matched.value.label
  }
  return hasValue.value ? String(props.value) : '--'
})

/** 标签色：未命中回退 info */
const tagType = computed<TagTypeName>(() => matched.value?.tagType ?? 'info')
</script>

<template>
  <el-tag v-if="hasValue" :type="tagType" disable-transitions>{{ label }}</el-tag>
  <span v-else>--</span>
</template>
