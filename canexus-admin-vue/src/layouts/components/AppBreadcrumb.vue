<script setup lang="ts">
/**
 * 面包屑：基于 route.matched 中带标题的层级生成。
 */
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { RouteLocationMatched } from 'vue-router'

const route = useRoute()

/** 面包屑项：仅保留有标题的路由层级 */
const breadcrumbs = computed<RouteLocationMatched[]>(() =>
  route.matched.filter((item) => item.meta?.title),
)
</script>

<template>
  <el-breadcrumb v-if="breadcrumbs.length > 1" class="app-breadcrumb" separator="/">
    <el-breadcrumb-item
      v-for="item in breadcrumbs"
      :key="item.path"
      :to="item.path !== route.path ? item.path : undefined"
    >
      {{ item.meta?.title }}
    </el-breadcrumb-item>
  </el-breadcrumb>
</template>
