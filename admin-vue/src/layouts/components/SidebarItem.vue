<script setup lang="ts">
/**
 * 侧边栏菜单项：根据路由记录递归渲染。
 *
 * 渲染规则（与若依一致）：
 * - 仅有一个可见子级时直接渲染该子级为菜单项
 * - 多个可见子级渲染为子菜单并递归
 * - 无子级渲染为叶子菜单项
 */
import { computed } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'

const props = defineProps<{
  /** 当前路由记录 */
  route: RouteRecordRaw
  /** 父级路径前缀 */
  basePath: string
}>()

/** 拼接完整路径（子路由相对路径基于父级前缀） */
function resolvePath(base: string, path: string): string {
  if (path.startsWith('/')) {
    return path
  }
  return `${base.replace(/\/$/, '')}/${path}`.replace(/\/{2,}/g, '/')
}

/** 当前节点完整路径（子级菜单 index 与递归前缀均基于该路径） */
const parentPath = computed(() => resolvePath(props.basePath, props.route.path))

/** 可见子路由 */
const visibleChildren = computed(() =>
  (props.route.children ?? []).filter((child) => !child.meta?.hidden),
)

/** 仅有一个可见子级时直接渲染该子级 */
const onlyChild = computed(() =>
  visibleChildren.value.length === 1 ? visibleChildren.value[0] : undefined,
)

/** 是否渲染为子菜单（多个可见子级） */
const isSubMenu = computed(() => visibleChildren.value.length > 1)
</script>

<template>
  <!-- 单一可见子级：直接渲染为菜单项（路径基于当前节点完整路径） -->
  <template v-if="onlyChild">
    <el-menu-item v-if="!onlyChild.children" :index="resolvePath(parentPath, onlyChild.path)">
      <AppIcon :name="onlyChild.meta?.icon" :size="16" />
      <template #title>{{ onlyChild.meta?.title }}</template>
    </el-menu-item>
    <SidebarItem v-else :route="onlyChild" :base-path="parentPath" />
  </template>
  <!-- 多个可见子级：渲染为子菜单 -->
  <el-sub-menu v-else-if="isSubMenu" :index="parentPath">
    <template #title>
      <AppIcon :name="route.meta?.icon" :size="16" />
      <span>{{ route.meta?.title }}</span>
    </template>
    <SidebarItem
      v-for="child in visibleChildren"
      :key="child.path"
      :route="child"
      :base-path="parentPath"
    />
  </el-sub-menu>
  <!-- 叶子菜单项 -->
  <el-menu-item v-else :index="parentPath">
    <AppIcon :name="route.meta?.icon" :size="16" />
    <template #title>{{ route.meta?.title }}</template>
  </el-menu-item>
</template>
