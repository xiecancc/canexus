<script setup lang="ts">
/**
 * 通用图标组件：按字符串名称动态渲染 @element-plus/icons-vue 图标。
 *
 * 用于菜单、面包屑、标签页等由配置驱动图标的场景；
 * 未匹配到图标名称时渲染占位图标（问号）。
 */
import type { Component } from 'vue'
import { computed } from 'vue'
import * as Icons from '@element-plus/icons-vue'
import { QuestionFilled } from '@element-plus/icons-vue'

const props = withDefaults(
  defineProps<{
    /** 图标名称（@element-plus/icons-vue 导出名，如 Search） */
    name?: string
    /** 图标尺寸（px 或任意 CSS 尺寸） */
    size?: number | string
    /** 图标颜色 */
    color?: string
  }>(),
  {
    name: '',
    size: 16,
    color: undefined,
  },
)

/** 全部图标集合（键为图标导出名） */
const iconMap = Icons as unknown as Record<string, Component | undefined>

/** 未匹配时的占位图标 */
const PLACEHOLDER = QuestionFilled

/** 解析命中的图标组件 */
const resolved = computed(() => (props.name ? (iconMap[props.name] ?? PLACEHOLDER) : PLACEHOLDER))
</script>

<template>
  <el-icon class="app-icon" :size="size" :color="color">
    <component :is="resolved" />
  </el-icon>
</template>
