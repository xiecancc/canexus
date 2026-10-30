<script setup lang="ts">
/**
 * 侧边栏：由路由表递归生成菜单，桌面端可折叠，移动端为抽屉式。
 * 菜单数据为布局路由的一级子路由（静态首页 + 守卫注册的动态路由）。
 */
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import { LAYOUT_ROUTE_NAME, constantRoutes } from '@/router'
import { useAppStore } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'
import SidebarItem from './SidebarItem.vue'

const appStore = useAppStore()
const authStore = useAuthStore()
const route = useRoute()

/** 是否移动端 */
const isMobile = computed(() => appStore.device === 'mobile')

/** 桌面端是否折叠 */
const isCollapsed = computed(() => appStore.sidebarCollapsed && !isMobile.value)

/** 菜单路由：布局一级子路由（静态 + 动态），过滤隐藏项 */
const menuRoutes = computed<RouteRecordRaw[]>(() => {
  const layout = constantRoutes.find((item) => item.name === LAYOUT_ROUTE_NAME)
  const staticChildren = (layout?.children ?? []).filter((item) => !item.meta?.hidden)
  const dynamicChildren = authStore.dynamicRoutes.filter((item) => !item.meta?.hidden)
  return [...staticChildren, ...dynamicChildren]
})

/** 当前高亮菜单 */
const activeMenu = computed(() => route.path)
</script>

<template>
  <aside
    class="sidebar"
    :class="{
      'is-collapsed': isCollapsed,
      'is-mobile': isMobile,
      'is-open': isMobile && !appStore.sidebarCollapsed,
    }"
  >
    <!-- Logo 区域 -->
    <div class="sidebar-logo">
      <span class="logo-title" v-show="!isCollapsed">Super Admin</span>
      <span class="logo-mini" v-show="isCollapsed">SA</span>
    </div>
    <!-- 菜单 -->
    <el-scrollbar class="sidebar-scroll">
      <el-menu
        class="sidebar-menu"
        mode="vertical"
        :collapse="isCollapsed"
        :collapse-transition="false"
        :default-active="activeMenu"
        :router="true"
      >
        <SidebarItem v-for="item in menuRoutes" :key="item.path" :route="item" base-path="" />
      </el-menu>
    </el-scrollbar>
  </aside>
</template>

<style lang="scss" scoped>
.sidebar {
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  z-index: 1001;
  display: flex;
  flex-direction: column;
  width: var(--sidebar-width);
  background-color: var(--sidebar-bg-color);
  transition:
    width 0.28s ease,
    transform 0.28s ease;
  overflow: hidden;

  &.is-collapsed {
    width: var(--sidebar-collapsed-width);
  }

  /* 移动端：抽屉式，默认移出屏幕 */
  &.is-mobile {
    width: var(--sidebar-width);
    transform: translateX(-100%);

    &.is-open {
      transform: translateX(0);
    }
  }
}

.sidebar-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  height: var(--navbar-height);
  overflow: hidden;
  white-space: nowrap;

  .logo-title {
    color: var(--sidebar-logo-text-color);
    font-size: 16px;
    font-weight: 600;
  }

  .logo-mini {
    color: var(--sidebar-logo-text-color);
    font-size: 16px;
    font-weight: 600;
  }
}

.sidebar-scroll {
  flex: 1;
  overflow: hidden;
}

.sidebar-menu {
  border-right: none;
}
</style>
