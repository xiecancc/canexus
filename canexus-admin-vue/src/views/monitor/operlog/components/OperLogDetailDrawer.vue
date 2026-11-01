<script setup lang="ts">
/**
 * 操作日志详情抽屉：展示单条操作日志全部字段，
 * 请求参数尝试 JSON 格式化展示（非 JSON 原样显示）。
 */
import { computed, ref } from 'vue'
import type { OperLogVO } from '@/api/monitor/operlog'
import { formatDateTime } from '@/utils/format'

defineOptions({ name: 'OperLogDetailDrawer' })

/** 抽屉显示状态 */
const visible = ref(false)

/** 当前查看的日志 */
const log = ref<OperLogVO | null>(null)

/** 状态文案：1 成功 0 失败 */
const statusLabel = computed(() => (log.value?.status === 1 ? '成功' : '失败'))

/** 请求参数格式化：合法 JSON 缩进展示，否则原样输出 */
const formattedParam = computed(() => {
  const value = log.value?.operParam
  if (!value) {
    return ''
  }
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
})

/** 打开抽屉 */
function open(row: OperLogVO): void {
  log.value = row
  visible.value = true
}

defineExpose({ open })
</script>

<template>
  <el-drawer
    v-model="visible"
    title="操作日志详情"
    size="640px"
    :append-to-body="true"
    class="oper-log-drawer"
  >
    <el-descriptions v-if="log" :column="2" border>
      <el-descriptions-item label="模块标题" :span="2">{{
        log.title || '--'
      }}</el-descriptions-item>
      <el-descriptions-item label="业务类型" :span="2">
        <DictTag dict-type="sys_oper_type" :value="log.businessType" />
      </el-descriptions-item>
      <el-descriptions-item label="操作人员">{{ log.operUserName || '--' }}</el-descriptions-item>
      <el-descriptions-item label="操作 IP">{{ log.operIp || '--' }}</el-descriptions-item>
      <el-descriptions-item label="请求方式">{{ log.requestMethod || '--' }}</el-descriptions-item>
      <el-descriptions-item label="状态">
        <el-tag :type="log.status === 1 ? 'success' : 'danger'" size="small">
          {{ statusLabel }}
        </el-tag>
      </el-descriptions-item>
      <el-descriptions-item label="请求 URL" :span="2">
        {{ log.requestUrl || '--' }}
      </el-descriptions-item>
      <el-descriptions-item label="耗时" :span="2">
        {{ log.costMs != null ? `${log.costMs} ms` : '--' }}
      </el-descriptions-item>
      <el-descriptions-item label="操作时间" :span="2">
        {{ formatDateTime(log.operTime) || '--' }}
      </el-descriptions-item>
      <el-descriptions-item label="调用方法" :span="2">
        <span class="mono-text">{{ log.method || '--' }}</span>
      </el-descriptions-item>
      <el-descriptions-item label="请求参数" :span="2">
        <pre class="param-pre">{{ formattedParam || '--' }}</pre>
      </el-descriptions-item>
      <el-descriptions-item label="错误消息" :span="2">
        <span :class="{ 'error-text': log.errorMsg }">{{ log.errorMsg || '--' }}</span>
      </el-descriptions-item>
    </el-descriptions>
  </el-drawer>
</template>

<style lang="scss" scoped>
.mono-text {
  font-family: var(--el-font-family-monospace, monospace);
  font-size: 12px;
  word-break: break-all;
}

.param-pre {
  margin: 0;
  padding: 8px;
  max-height: 260px;
  overflow: auto;
  font-family: var(--el-font-family-monospace, monospace);
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  background: var(--el-fill-color-light);
  border-radius: 4px;
}

.error-text {
  color: var(--el-color-danger);
}
</style>
