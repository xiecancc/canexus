<script setup lang="ts">
/**
 * 顶部导航：面包屑、主题色选择、主题模式、暗黑切换、全屏、用户菜单。
 */
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useFullscreen } from '@vueuse/core'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import AppBreadcrumb from './AppBreadcrumb.vue'
import { CHANGE_PASSWORD_ROUTE_PATH } from '@/router/constants'
import { DEFAULT_PRIMARY, useAppStore } from '@/stores/app'
import type { ThemeMode } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'

const appStore = useAppStore()
const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

const { isFullscreen, toggle: toggleFullscreen } = useFullscreen()

/** 预设主题色 */
const predefineColors = ['#409eff', '#0d9488', '#7c3aed', '#e11d48', '#f59e0b', '#16a34a']

/** 主题模式选项 */
const themeModeOptions: Array<{ label: string; value: ThemeMode; icon: string }> = [
  { label: '跟随系统', value: 'auto', icon: 'Monitor' },
  { label: '亮色模式', value: 'light', icon: 'Sunny' },
  { label: '暗色模式', value: 'dark', icon: 'Moon' },
]

/** 当前主题模式对应图标 */
const themeModeIcon = computed(
  () => themeModeOptions.find((option) => option.value === appStore.themeMode)?.icon ?? 'Monitor',
)

/** 修改主题色（取消选择时回退默认色） */
function handlePrimaryChange(color: string | null): void {
  appStore.setPrimary(color ?? DEFAULT_PRIMARY)
}

/** 用户昵称首字（无头像时展示） */
const nicknameText = computed(() => authStore.nickName.charAt(0) || '用')

/** 用户下拉命令 */
function handleUserCommand(command: string): void {
  if (command === 'profile') {
    // 个人中心暂为占位功能
    ElMessage.info('个人中心建设中')
  } else if (command === 'change-password') {
    // 自助改密：后端改密成功后会吊销全部刷新令牌并强制重新登录
    router.push(CHANGE_PASSWORD_ROUTE_PATH)
  } else if (command === 'logout') {
    handleLogout()
  }
}

/** 退出登录：调用登出接口清理令牌与动态路由后回登录页 */
async function handleLogout(): Promise<void> {
  try {
    await authStore.logout()
    ElMessage.success('已退出登录')
    router.push('/login')
  } catch {
    // resetAuth 保证本地状态已清理，异常时兜底跳转
    router.push('/login')
  }
}
</script>

<template>
  <header class="navbar">
    <div class="navbar-left">
      <el-tooltip content="折叠 / 展开侧边栏" placement="bottom">
        <button class="navbar-action" type="button" @click="appStore.toggleSidebar()">
          <AppIcon :name="appStore.sidebarCollapsed ? 'Expand' : 'Fold'" :size="18" />
        </button>
      </el-tooltip>
      <AppBreadcrumb class="navbar-breadcrumb" />
    </div>
    <div class="navbar-right">
      <!-- 主题色选择 -->
      <el-color-picker
        :model-value="appStore.primary"
        :predefine="predefineColors"
        size="small"
        @update:model-value="handlePrimaryChange"
      />
      <!-- 主题模式（跟随系统 / 亮色 / 暗色） -->
      <el-dropdown trigger="click" @command="appStore.setThemeMode">
        <button class="navbar-action" type="button">
          <AppIcon :name="themeModeIcon" :size="18" />
        </button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item
              v-for="option in themeModeOptions"
              :key="option.value"
              :command="option.value"
              :disabled="option.value === appStore.themeMode"
            >
              {{ option.label }}
            </el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <!-- 暗黑切换开关 -->
      <el-switch v-model="appStore.isDark" inline-prompt aria-label="切换暗黑模式">
        <template #active-action>
          <AppIcon name="Moon" :size="12" color="#ffffff" />
        </template>
        <template #inactive-action>
          <AppIcon name="Sunny" :size="12" color="#606266" />
        </template>
      </el-switch>
      <!-- 全屏 -->
      <el-tooltip :content="isFullscreen ? '退出全屏' : '全屏'" placement="bottom">
        <button class="navbar-action" type="button" @click="toggleFullscreen()">
          <AppIcon :name="isFullscreen ? 'Aim' : 'FullScreen'" :size="18" />
        </button>
      </el-tooltip>
      <!-- 用户菜单 -->
      <el-dropdown trigger="click" @command="handleUserCommand">
        <button class="navbar-action navbar-user" type="button">
          <el-avatar :size="28" :src="authStore.avatar || undefined">
            {{ nicknameText }}
          </el-avatar>
        </button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="profile">个人中心</el-dropdown-item>
            <el-dropdown-item command="change-password">修改密码</el-dropdown-item>
            <el-dropdown-item divided command="logout">退出登录</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
    </div>
  </header>
</template>

<style lang="scss" scoped>
.navbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: var(--navbar-height);
  padding: 0 12px;
  background-color: var(--el-bg-color);
  border-bottom: 1px solid var(--el-border-color-light);
}

.navbar-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.navbar-breadcrumb {
  margin-left: 4px;
}

.navbar-right {
  display: flex;
  align-items: center;
  gap: 12px;
}

.navbar-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 32px;
  padding: 0 6px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  background: none;
  border: none;
  border-radius: 4px;
  transition: background-color 0.2s ease;

  &:hover {
    background-color: var(--el-fill-color);
  }
}

.navbar-user {
  padding: 0 2px;
  border-radius: 50%;
}
</style>
