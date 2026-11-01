<script setup lang="ts">
/**
 * 仪表盘：框架欢迎信息、占位统计卡片与最新公告卡片（latest 接口，缺省 5 条）。
 */
import { onMounted, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import type { NoticeBrief } from '@/api/system/notice'
import { getLatestNoticesApi } from '@/api/system/notice'
import { formatDateTime } from '@/utils/format'
import { getDictLabel, getDictTagType, useNoticeTypeDict } from '@/utils/dict'

defineOptions({ name: 'Dashboard' })

/** 公告类型字典（useDict('sys_notice_type')，未拉取到时回退本地常量） */
const noticeTypeItems = useNoticeTypeDict()

/** 占位统计卡片（数据待接入后端后填充） */
const statCards = [
  { title: '用户总数', value: '--', icon: 'User' },
  { title: '角色总数', value: '--', icon: 'UserFilled' },
  { title: '今日登录', value: '--', icon: 'Histogram' },
  { title: '系统通知', value: '--', icon: 'Bell' },
] as const

/** 最新公告列表（空态展示"暂无公告"） */
const notices = ref<NoticeBrief[]>([])

/** 公告加载中 */
const noticesLoading = ref(false)

/** 加载最新公告（失败静默，错误提示在请求层） */
async function loadNotices(): Promise<void> {
  noticesLoading.value = true
  try {
    notices.value = (await getLatestNoticesApi(5)) ?? []
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    noticesLoading.value = false
  }
}

/** 点击公告行：latest 接口不含全文，简化为弹窗展示标题与发布时间 */
function handleNoticeClick(notice: NoticeBrief): void {
  void ElMessageBox.alert(`发布时间：${formatDateTime(notice.createdAt) || '--'}`, notice.title, {
    confirmButtonText: '知道了',
  })
}

onMounted(() => {
  void loadNotices()
})
</script>

<template>
  <div class="dashboard">
    <el-card shadow="never" class="welcome-card">
      <template #header>
        <div class="card-header">
          <AppIcon name="Platform" :size="18" />
          <span>欢迎使用 Super Admin 后台管理框架</span>
        </div>
      </template>
      <p>这是一套对标若依（RuoYi）重构的后台管理框架基座，采用前后端分离架构。</p>
      <p>
        前端技术栈：Vite 8 · Vue 3 · TypeScript · Pinia · Vue Router · Element Plus · VueUse ·
        Axios。
      </p>
      <p>当前为仪表盘占位页，系统管理等功能模块将陆续接入。</p>
    </el-card>
    <el-row :gutter="16" class="stat-row">
      <el-col v-for="card in statCards" :key="card.title" :xs="12" :sm="12" :md="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-content">
            <AppIcon :name="card.icon" :size="28" class="stat-icon" />
            <div class="stat-info">
              <div class="stat-value">{{ card.value }}</div>
              <div class="stat-title">{{ card.title }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
    <el-row :gutter="16" class="notice-row">
      <el-col :span="24">
        <el-card v-loading="noticesLoading" shadow="never">
          <template #header>
            <div class="card-header">
              <AppIcon name="Bell" :size="18" />
              <span>最新公告</span>
            </div>
          </template>
          <template v-if="notices.length">
            <div
              v-for="notice in notices"
              :key="notice.id"
              class="notice-item"
              @click="handleNoticeClick(notice)"
            >
              <div class="notice-main">
                <el-tag
                  :type="getDictTagType(noticeTypeItems, notice.noticeType)"
                  size="small"
                  disable-transitions
                  class="notice-tag"
                >
                  {{ getDictLabel(noticeTypeItems, notice.noticeType, '--') }}
                </el-tag>
                <span class="notice-title">{{ notice.title }}</span>
              </div>
              <span class="notice-time">{{ formatDateTime(notice.createdAt) }}</span>
            </div>
          </template>
          <el-empty v-else description="暂无公告" :image-size="60" />
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style lang="scss" scoped>
.card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}

.stat-row {
  margin-top: 16px;
}

.notice-row {
  margin-top: 16px;
}

.stat-content {
  display: flex;
  align-items: center;
  gap: 12px;
}

.stat-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  color: var(--el-color-primary);
  background-color: var(--el-color-primary-light-9);
  border-radius: 8px;
}

.stat-value {
  font-size: 20px;
  font-weight: 600;
}

.stat-title {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.notice-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 4px;
  cursor: pointer;
  border-bottom: 1px solid var(--el-border-color-lighter);

  &:last-child {
    border-bottom: none;
  }

  &:hover .notice-title {
    color: var(--el-color-primary);
  }
}

.notice-main {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.notice-tag {
  flex-shrink: 0;
}

.notice-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  transition: color 0.2s;
}

.notice-time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
