<script setup lang="ts">
/**
 * 图标选择器：弹出面板内按 @element-plus/icons-vue 导出名网格展示，
 * 支持关键字搜索、分页浏览、选中高亮与一键清空。
 * 触发器模拟输入框外观，未选择时显示占位文案。
 */
import { computed, ref, watch } from 'vue'
import { CircleClose, Search } from '@element-plus/icons-vue'
import AppIcon from './AppIcon.vue'
import { filterIconNames } from '@/utils/icons'

/** 组件名（keep-alive / 调试用） */
defineOptions({ name: 'IconPicker' })

const props = withDefaults(
  defineProps<{
    /** 选中的图标名称（v-model） */
    modelValue?: string
    /** 未选择时的占位文案 */
    placeholder?: string
    /** 弹层是否插入至 body 元素（默认 true） */
    teleported?: boolean
  }>(),
  {
    modelValue: '',
    placeholder: '请选择图标',
    teleported: true,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

/** 弹出面板每页图标数量（8 列 × 4 行） */
const PAGE_SIZE = 32

/** 弹层显示状态 */
const visible = ref(false)

/** 搜索关键字 */
const keyword = ref('')

/** 当前页码（从 1 开始） */
const pageIndex = ref(1)

/** 过滤后的图标名称列表 */
const filteredNames = computed(() => filterIconNames(keyword.value))

/** 当前页展示的图标名称 */
const pagedNames = computed(() => {
  const start = (pageIndex.value - 1) * PAGE_SIZE
  return filteredNames.value.slice(start, start + PAGE_SIZE)
})

// 搜索关键字变化时回到第一页，避免停留在超出范围的页码
watch(keyword, () => {
  pageIndex.value = 1
})

/** 选中图标：回传名称并关闭弹层 */
function handleSelect(name: string): void {
  emit('update:modelValue', name)
  visible.value = false
}

/** 清空选中（阻止冒泡避免触发弹层打开） */
function handleClear(): void {
  emit('update:modelValue', '')
}
</script>

<template>
  <el-popover
    v-model:visible="visible"
    trigger="click"
    placement="bottom-start"
    :width="360"
    :teleported="props.teleported"
  >
    <template #reference>
      <div class="icon-picker-trigger">
        <template v-if="modelValue">
          <AppIcon :name="modelValue" :size="18" class="trigger-icon" />
          <span class="trigger-name">{{ modelValue }}</span>
          <el-icon class="trigger-clear" title="清空图标" @click.stop="handleClear">
            <CircleClose />
          </el-icon>
        </template>
        <span v-else class="trigger-placeholder">{{ placeholder }}</span>
      </div>
    </template>

    <div class="icon-picker-panel">
      <el-input
        v-model="keyword"
        placeholder="搜索图标名称（英文）"
        clearable
        :prefix-icon="Search"
      />
      <div v-if="pagedNames.length > 0" class="icon-grid">
        <button
          v-for="name in pagedNames"
          :key="name"
          type="button"
          class="icon-cell"
          :class="{ 'is-active': name === modelValue }"
          :title="name"
          @click="handleSelect(name)"
        >
          <AppIcon :name="name" :size="20" />
        </button>
      </div>
      <el-empty v-else :image-size="60" description="未找到匹配图标" />
      <el-pagination
        v-model:current-page="pageIndex"
        class="icon-pagination"
        :total="filteredNames.length"
        :page-size="PAGE_SIZE"
        layout="prev, pager, next"
        size="small"
        background
      />
    </div>
  </el-popover>
</template>

<style lang="scss" scoped>
/* 触发器：模拟输入框外观，适配暗黑模式 */
.icon-picker-trigger {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 32px;
  padding: 0 8px;
  font-size: 14px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  background-color: var(--el-fill-color-blank);
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  transition: border-color 0.2s;

  &:hover {
    border-color: var(--el-border-color-hover);

    .trigger-clear {
      opacity: 1;
    }
  }

  .trigger-icon {
    flex-shrink: 0;
    color: var(--el-text-color-regular);
  }

  .trigger-name {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .trigger-clear {
    flex-shrink: 0;
    color: var(--el-text-color-placeholder);
    opacity: 0;
    transition: opacity 0.2s;

    &:hover {
      color: var(--el-text-color-secondary);
    }
  }

  .trigger-placeholder {
    color: var(--el-text-color-placeholder);
  }
}

.icon-picker-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.icon-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 4px;
  max-height: 236px;
  overflow-y: auto;
}

.icon-cell {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 36px;
  padding: 0;
  color: var(--el-text-color-regular);
  cursor: pointer;
  background-color: transparent;
  border: none;
  border-radius: 4px;
  transition:
    background-color 0.15s,
    color 0.15s;

  &:hover {
    color: var(--el-color-primary);
    background-color: var(--el-fill-color-light);
  }

  &.is-active {
    color: var(--el-color-primary);
    background-color: var(--el-color-primary-light-9);
    outline: 1px solid var(--el-color-primary-light-5);
  }
}

.icon-pagination {
  align-self: center;
}
</style>
