<script setup lang="ts">
/**
 * 404 页面：访问不存在的路由时展示。
 */
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="not-found">
    <h1 class="not-found-code">404</h1>
    <p class="not-found-message">抱歉，您访问的页面不存在</p>
    <el-button type="primary" @click="router.push('/')">返回首页</el-button>
  </div>
</template>

<style lang="scss" scoped>
.not-found {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100vh;
  gap: 12px;
}

.not-found-code {
  margin: 0;
  font-size: 72px;
  font-weight: 700;
  color: var(--el-color-primary);
}

.not-found-message {
  margin: 0 0 12px;
  color: var(--el-text-color-secondary);
}
</style>
