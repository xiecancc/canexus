<script setup lang="ts">
/**
 * 任务执行日志抽屉：按任务过滤分页展示执行记录，
 * 支持执行状态筛选；异常信息默认红字截断，点击展开 / 收起。
 */
import { ref } from 'vue'
import type { JobLogVO, JobVO } from '@/api/monitor/job'
import { getJobLogPageApi } from '@/api/monitor/job'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'

defineOptions({ name: 'JobLogDrawer' })

/** 抽屉显示状态 */
const visible = ref(false)

/** 当前查看的任务 */
const job = ref<JobVO | null>(null)

/** 日志分页查询条件（jobId 由打开时注入） */
interface JobLogQuery {
  status?: number
}

/** 当前任务 ID（fetcher 闭包使用，GUID 字符串） */
let currentJobId = ''

const {
  loading,
  pageIndex,
  pageSize,
  total,
  items,
  query,
  fetchData,
  handleSearch,
  handleReset,
  handleSizeChange,
  handlePageChange,
} = usePage<JobLogQuery, JobLogVO>({
  fetcher: (params) => getJobLogPageApi({ ...params, jobId: currentJobId }),
  getDefaultQuery: () => ({}),
  immediate: false,
})

/** 已展开异常信息的日志 ID 集合 */
const expandedIds = ref<string[]>([])

/** 切换异常信息展开 / 收起 */
function toggleException(id: string): void {
  expandedIds.value = expandedIds.value.includes(id)
    ? expandedIds.value.filter((item) => item !== id)
    : [...expandedIds.value, id]
}

/** 打开抽屉并加载该任务的执行日志 */
function open(row: JobVO): void {
  job.value = row
  currentJobId = row.id
  expandedIds.value = []
  visible.value = true
  handleReset()
}

defineExpose({ open })
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="job ? `执行日志 - ${job.jobName}` : '执行日志'"
    size="900px"
    :append-to-body="true"
  >
    <!-- 状态筛选 -->
    <el-form inline @submit.prevent="handleSearch">
      <el-form-item label="执行状态">
        <el-select
          v-model="query.status"
          placeholder="全部"
          clearable
          style="width: 130px"
          @change="handleSearch"
        >
          <el-option label="成功" :value="1" />
          <el-option label="失败" :value="0" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="handleSearch">搜索</el-button>
        <el-button @click="handleReset">重置</el-button>
      </el-form-item>
    </el-form>

    <!-- 日志表格 -->
    <el-table
      v-loading="loading"
      :data="items"
      row-key="id"
      :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
    >
      <el-table-column label="执行时间" width="170">
        <template #default="{ row }">{{ formatDateTime((row as JobLogVO).triggerTime) }}</template>
      </el-table-column>
      <el-table-column label="耗时" width="90" align="right">
        <template #default="{ row }">
          {{ (row as JobLogVO).costMs != null ? `${(row as JobLogVO).costMs} ms` : '--' }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="80" align="center">
        <template #default="{ row }">
          <el-tag :type="(row as JobLogVO).status === 1 ? 'success' : 'danger'" size="small">
            {{ (row as JobLogVO).status === 1 ? '成功' : '失败' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="结果" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ (row as JobLogVO).result || '--' }}</template>
      </el-table-column>
      <el-table-column label="异常" min-width="220">
        <template #default="{ row }">
          <template v-if="(row as JobLogVO).exception">
            <div
              class="exception-text"
              :class="{ expanded: expandedIds.includes((row as JobLogVO).id) }"
              @click="toggleException((row as JobLogVO).id)"
            >
              {{ (row as JobLogVO).exception }}
            </div>
          </template>
          <span v-else>--</span>
        </template>
      </el-table-column>
    </el-table>

    <!-- 分页 -->
    <el-pagination
      v-model:current-page="pageIndex"
      v-model:page-size="pageSize"
      class="table-pagination"
      :total="total"
      :page-sizes="[10, 20]"
      layout="total, sizes, prev, pager, next, jumper"
      @size-change="handleSizeChange"
      @current-change="handlePageChange"
    />
  </el-drawer>
</template>

<style lang="scss" scoped>
.exception-text {
  color: var(--el-color-danger);
  font-size: 12px;
  line-height: 1.5;
  cursor: pointer;
  word-break: break-all;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;

  &.expanded {
    -webkit-line-clamp: unset;
    overflow: visible;
  }
}

.table-pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
