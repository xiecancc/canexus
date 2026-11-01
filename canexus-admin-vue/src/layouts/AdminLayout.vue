<script setup lang="ts">
/**
 * 后台经典布局：左侧边栏 + 顶部导航 + 标签页 + 主内容区。
 * 移动端侧边栏切换为抽屉式（带遮罩）。
 */
import { computed, watch } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { useAppStore } from '@/stores/app'
import AppSidebar from './components/AppSidebar.vue'
import AppNavbar from './components/AppNavbar.vue'
import AppTags from './components/AppTags.vue'
import AppMain from './components/AppMain.vue'

const appStore = useAppStore()

/** 移动端断点宽度（px） */
const MOBILE_BREAKPOINT = 992

const { width } = useWindowSize()

/** 是否移动端 */
const isMobile = computed(() => appStore.device === 'mobile')

/** 桌面端侧边栏是否折叠 */
const isDesktopCollapsed = computed(() => !isMobile.value && appStore.sidebarCollapsed)

// 初始化设备类型（首次不改变用户侧边栏偏好）
appStore.device = width.value < MOBILE_BREAKPOINT ? 'mobile' : 'desktop'

// 窗口尺寸跨越断点时切换设备类型，并联动侧边栏状态
watch(width, (value, oldValue) => {
  const before = oldValue < MOBILE_BREAKPOINT
  const after = value < MOBILE_BREAKPOINT
  if (before === after) {
    return
  }
  appStore.device = after ? 'mobile' : 'desktop'
  appStore.sidebarCollapsed = after
})
</script>

<template>
  <div class="admin-layout">
    <!-- 侧边栏 -->
    <AppSidebar />
    <!-- 移动端遮罩 -->
    <Transition name="fade">
      <div
        v-if="isMobile && !appStore.sidebarCollapsed"
        class="layout-mask"
        @click="appStore.sidebarCollapsed = true"
      />
    </Transition>
    <!-- 主体区域 -->
    <div class="layout-main" :class="{ 'is-collapsed': isDesktopCollapsed, 'is-mobile': isMobile }">
      <AppNavbar />
      <AppTags />
      <AppMain />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.admin-layout {
  height: 100%;
}

.layout-mask {
  position: fixed;
  inset: 0;
  z-index: 1000;
  background-color: rgba(0, 0, 0, 0.45);
}

.layout-main {
  min-height: 100%;
  margin-left: var(--sidebar-width);
  transition: margin-left 0.28s ease;

  &.is-collapsed {
    margin-left: var(--sidebar-collapsed-width);
  }

  &.is-mobile {
    margin-left: 0;
  }
}
</style>
