<script setup lang="ts">
/**
 * 403 页面：访问无权限的路由时展示，支持返回首页与上一页。
 */
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="forbidden">
    <h1 class="forbidden-code">403</h1>
    <p class="forbidden-message">抱歉，您没有权限访问该页面</p>
    <div class="forbidden-actions">
      <el-button type="primary" @click="router.push('/')">返回首页</el-button>
      <el-button @click="router.back()">返回上一页</el-button>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.forbidden {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100vh;
  gap: 12px;
}

.forbidden-code {
  margin: 0;
  font-size: 72px;
  font-weight: 700;
  color: var(--el-color-warning);
}

.forbidden-message {
  margin: 0 0 12px;
  color: var(--el-text-color-secondary);
}

.forbidden-actions {
  display: flex;
  gap: 12px;
}
</style>
