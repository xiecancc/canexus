<script setup lang="ts">
/**
 * 应用根组件：仅负责路由出口与主题初始化。
 */
import { useTheme } from '@/composables/useTheme'

// 初始化主题（暗黑模式 + 主题色），根组件常驻，副作用随应用生命周期存在
useTheme()
</script>

<template>
  <router-view />
</template>
