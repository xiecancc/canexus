<script setup lang="ts">
/**
 * 主内容区：router-view + 过渡动画 + keep-alive 缓存（meta.keepAlive）。
 */
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

/** 需要缓存的组件名称：meta.keepAlive 为 true 且有 name 的路由 */
const cachedViews = computed(() =>
  router
    .getRoutes()
    .filter((item) => item.meta?.keepAlive && item.name)
    .map((item) => String(item.name)),
)
</script>

<template>
  <section class="app-main">
    <router-view v-slot="{ Component, route: viewRoute }">
      <transition name="fade-transform" mode="out-in">
        <keep-alive :include="cachedViews">
          <component :is="Component" v-if="Component" :key="viewRoute.path" />
        </keep-alive>
      </transition>
    </router-view>
  </section>
</template>

<style lang="scss" scoped>
.app-main {
  min-height: calc(100vh - var(--navbar-height) - var(--tags-height));
  padding: var(--app-main-padding);
  background-color: var(--el-bg-color-page);
}
</style>
