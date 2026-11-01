<script setup lang="ts">
/**
 * 通知公告查看抽屉：经详情接口拉取全文，
 * 展示标题、类型标签、状态、创建时间与公告全文。
 */
import { computed, ref } from 'vue'
import type { NoticeVO } from '@/api/system/notice'
import { getNoticeApi } from '@/api/system/notice'
import { formatDateTime } from '@/utils/format'
import { getDictLabel, getDictTagType, useNoticeTypeDict } from '@/utils/dict'

defineOptions({ name: 'NoticeDetailDrawer' })

/** 抽屉显示状态 */
const visible = ref(false)

/** 详情加载中 */
const loading = ref(false)

/** 当前查看的公告（详情接口数据，失败时回退行数据） */
const notice = ref<NoticeVO | null>(null)

/** 公告类型字典（useDict('sys_notice_type')，未拉取到时回退本地常量） */
const noticeTypeItems = useNoticeTypeDict()

/** 公告类型标签文案 */
const typeLabel = computed(() =>
  getDictLabel(noticeTypeItems.value, notice.value?.noticeType, '--'),
)

/** 公告类型标签色 */
const typeTagType = computed(() => getDictTagType(noticeTypeItems.value, notice.value?.noticeType))

/** 打开抽屉并拉取全文 */
async function open(row: NoticeVO): Promise<void> {
  notice.value = row
  visible.value = true
  loading.value = true
  try {
    notice.value = await getNoticeApi(row.id)
  } catch {
    // 详情拉取失败时保留列表行数据展示（错误提示在请求层）
  } finally {
    loading.value = false
  }
}

defineExpose({ open })
</script>

<template>
  <el-drawer v-model="visible" title="公告详情" size="560px" :append-to-body="true">
    <el-skeleton v-if="loading" :rows="6" animated />
    <template v-else-if="notice">
      <h2 class="notice-title">{{ notice.title }}</h2>
      <div class="notice-meta">
        <el-tag :type="typeTagType" size="small" disable-transitions>{{ typeLabel }}</el-tag>
        <span class="meta-item">{{ formatDateTime(notice.createdAt) }}</span>
      </div>
      <el-divider />
      <div class="notice-content">{{ notice.content || '--' }}</div>
      <div v-if="notice.description" class="notice-description">备注：{{ notice.description }}</div>
    </template>
  </el-drawer>
</template>

<style lang="scss" scoped>
.notice-title {
  margin: 0 0 12px;
  font-size: 18px;
  font-weight: 600;
  line-height: 1.4;
  word-break: break-all;
}

.notice-meta {
  display: flex;
  align-items: center;
  gap: 12px;
}

.meta-item {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.notice-content {
  font-size: 14px;
  line-height: 1.8;
  white-space: pre-wrap;
  word-break: break-word;
}

.notice-description {
  margin-top: 24px;
  padding: 8px 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-light);
  border-radius: 4px;
}
</style>
