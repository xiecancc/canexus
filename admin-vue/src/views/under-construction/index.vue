<script setup lang="ts">
/**
 * 建设中占位页：动态路由组件映射不到时的回落页面。
 */
defineOptions({ name: 'UnderConstruction' })
</script>

<template>
  <div class="under-construction">
    <AppIcon name="Suitcase" :size="56" class="construction-icon" />
    <p class="construction-title">页面建设中</p>
    <p class="construction-desc">该功能模块正在开发中，敬请期待</p>
  </div>
</template>

<style lang="scss" scoped>
.under-construction {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - var(--navbar-height) - var(--tags-height) - 2 * var(--app-main-padding));
  gap: 8px;
}

.construction-icon {
  margin-bottom: 12px;
  color: var(--el-text-color-placeholder);
}

.construction-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.construction-desc {
  margin: 0;
  font-size: 14px;
  color: var(--el-text-color-secondary);
}
</style>
