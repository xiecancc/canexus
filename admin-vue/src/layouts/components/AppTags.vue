<script setup lang="ts">
/**
 * 标签页导航：访问带标题的路由时生成标签，支持点击切换、关闭、右键"关闭其他"。
 * 首页（affix）标签固定不可关闭。标签状态随布局常驻，无需持久化。
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'

/** 标签视图数据 */
interface TagView {
  /** 路由完整路径（唯一标识） */
  path: string
  /** 标签标题 */
  title: string
  /** 图标名称 */
  icon?: string
  /** 是否固定（不可关闭） */
  affix?: boolean
}

const route = useRoute()
const router = useRouter()

/** 已访问标签（首页固定） */
const tags = ref<TagView[]>([
  { path: '/dashboard', title: '仪表盘', icon: 'Odometer', affix: true },
])

/** 追加当前路由对应标签 */
function addTag(): void {
  if (!route.meta?.title) {
    return
  }
  if (tags.value.some((tag) => tag.path === route.fullPath)) {
    return
  }
  tags.value.push({
    path: route.fullPath,
    title: String(route.meta.title),
    icon: route.meta.icon,
    affix: route.meta.affix,
  })
}

watch(() => route.fullPath, addTag, { immediate: true })

/** 点击标签切换路由 */
function clickTag(tag: TagView): void {
  if (tag.path !== route.path) {
    router.push(tag.path)
  }
}

/** 关闭标签；若关闭的是当前标签则跳转到最后一个标签 */
function closeTag(tag: TagView): void {
  if (tag.affix) {
    return
  }
  const index = tags.value.findIndex((item) => item.path === tag.path)
  if (index === -1) {
    return
  }
  tags.value.splice(index, 1)
  if (tag.path === route.fullPath) {
    const last = tags.value[tags.value.length - 1]
    router.push(last?.path ?? '/dashboard')
  }
}

/** 右键菜单状态 */
const contextMenuVisible = ref(false)
const contextMenuPosition = ref({ left: 0, top: 0 })
const contextMenuTag = ref<TagView | null>(null)

/** 打开右键菜单 */
function openContextMenu(tag: TagView, event: MouseEvent): void {
  contextMenuTag.value = tag
  contextMenuPosition.value = { left: event.clientX, top: event.clientY }
  contextMenuVisible.value = true
}

/** 关闭其他标签（保留固定标签与右键目标标签） */
function closeOtherTags(): void {
  const target = contextMenuTag.value
  if (!target) {
    return
  }
  tags.value = tags.value.filter((tag) => tag.affix || tag.path === target.path)
  // 当前路由对应的标签被关闭时，回跳到保留的最后一个标签
  if (!tags.value.some((tag) => tag.path === route.fullPath)) {
    const last = tags.value[tags.value.length - 1]
    router.push(last?.path ?? '/dashboard')
  }
  contextMenuVisible.value = false
}

/** 点击页面其他区域时关闭右键菜单 */
function hideContextMenu(): void {
  contextMenuVisible.value = false
}

onMounted(() => {
  document.addEventListener('click', hideContextMenu)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', hideContextMenu)
})
</script>

<template>
  <div class="tags-view">
    <el-scrollbar>
      <div class="tags-list">
        <span
          v-for="tag in tags"
          :key="tag.path"
          class="tag-item"
          :class="{ 'is-active': tag.path === route.fullPath }"
          @click="clickTag(tag)"
          @contextmenu.prevent="openContextMenu(tag, $event)"
        >
          <AppIcon v-if="tag.icon" :name="tag.icon" :size="12" class="tag-icon" />
          <span class="tag-title">{{ tag.title }}</span>
          <AppIcon
            v-if="!tag.affix"
            name="Close"
            :size="12"
            class="tag-close"
            @click.stop="closeTag(tag)"
          />
        </span>
      </div>
    </el-scrollbar>
    <!-- 右键菜单 -->
    <ul
      v-show="contextMenuVisible"
      class="context-menu"
      :style="{ left: `${contextMenuPosition.left}px`, top: `${contextMenuPosition.top}px` }"
    >
      <li @click="closeOtherTags()">关闭其他</li>
    </ul>
  </div>
</template>

<style lang="scss" scoped>
.tags-view {
  position: relative;
  height: var(--tags-height);
  background-color: var(--el-bg-color);
  border-bottom: 1px solid var(--el-border-color-light);
}

.tags-list {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 100%;
  padding: 0 12px;
  white-space: nowrap;
}

.tag-item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 24px;
  padding: 0 8px;
  font-size: 12px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  background-color: var(--el-fill-color-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 3px;
  transition: all 0.2s ease;

  &:hover {
    color: var(--el-color-primary);
  }

  &.is-active {
    color: var(--el-color-primary);
    background-color: var(--el-color-primary-light-9);
    border-color: var(--el-color-primary-light-7);

    &::before {
      width: 8px;
      height: 8px;
      background-color: var(--el-color-primary);
      border-radius: 50%;
      content: '';
    }
  }
}

.tag-close {
  padding: 1px;
  border-radius: 50%;

  &:hover {
    color: #ffffff;
    background-color: var(--el-text-color-placeholder);
  }
}

.context-menu {
  position: fixed;
  z-index: 3000;
  margin: 0;
  padding: 4px 0;
  font-size: 12px;
  list-style: none;
  background-color: var(--el-bg-color-overlay);
  border: 1px solid var(--el-border-color-light);
  border-radius: 4px;
  box-shadow: var(--el-box-shadow-light);

  li {
    padding: 6px 16px;
    cursor: pointer;

    &:hover {
      color: var(--el-color-primary);
      background-color: var(--el-fill-color);
    }
  }
}
</style>
