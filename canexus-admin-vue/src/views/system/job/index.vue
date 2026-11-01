<script setup lang="ts">
/**
 * 定时任务管理：关键字 / 状态搜索，行内运行 / 暂停切换（失败回滚）、
 * 新增 / 编辑、立即执行（确认后触发）、执行日志抽屉与删除。
 *
 * 权限说明：前后端已统一收敛为 monitor:job:*——状态切换归 edit（与用户管理行内开关一致），
 * 执行日志查看归 query（与操作日志详情一致），立即执行使用独立 run 标识。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CaretRight, Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { JobVO } from '@/api/monitor/job'
import { deleteJobApi, getJobPageApi, runJobApi, updateJobStatusApi } from '@/api/monitor/job'
import { usePage } from '@/composables/usePage'
import { usePermission } from '@/composables/usePermission'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { formatDateTime } from '@/utils/format'
import JobFormDialog from './components/JobFormDialog.vue'
import JobLogDrawer from './components/JobLogDrawer.vue'

defineOptions({ name: 'Job' })

const { hasPerm } = usePermission()

/** 定时任务分页查询条件 */
interface JobQuery {
  keyword: string
  status?: number
}

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
} = usePage<JobQuery, JobVO>({
  fetcher: getJobPageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 行内切换运行 / 暂停：成功轻提示，失败由组合式回滚（错误提示在请求层） */
async function handleStatusChange(row: JobVO): Promise<void> {
  const success = await commitStatusToggle(row, (status) => updateJobStatusApi(row.id, status))
  if (success) {
    ElMessage.success(row.status === 1 ? '已恢复运行' : '已暂停')
  }
}

/** 立即执行一次（确认后触发） */
async function handleRun(row: JobVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认立即执行一次任务「${row.jobName}」？`,
    '执行确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await runJobApi(row.id)
  ElMessage.success('已触发')
}

/** 删除定时任务（当前页删空时自动回退一页） */
async function handleDelete(row: JobVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除任务「${row.jobName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteJobApi(row.id)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/** 新增 / 编辑对话框与执行日志抽屉引用 */
const formDialogRef = ref<InstanceType<typeof JobFormDialog>>()
const logDrawerRef = ref<InstanceType<typeof JobLogDrawer>>()
</script>

<template>
  <div class="job-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="任务名称 / 任务分组"
            clearable
            style="width: 220px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select
            v-model="query.status"
            placeholder="全部"
            clearable
            style="width: 140px"
            @change="handleSearch"
          >
            <el-option label="运行" :value="1" />
            <el-option label="暂停" :value="0" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">搜索</el-button>
          <el-button :icon="Refresh" @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 操作栏 -->
      <div class="table-toolbar">
        <el-button
          v-permission="'monitor:job:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 任务表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="任务名称" prop="jobName" min-width="140" show-overflow-tooltip />
        <el-table-column label="任务分组" prop="jobGroup" min-width="110" show-overflow-tooltip />
        <el-table-column label="Cron表达式" prop="cronExpression" min-width="130">
          <template #default="{ row }">
            <span class="cron-text">{{ (row as JobVO).cronExpression }}</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              :disabled="!hasPerm('monitor:job:edit')"
              @change="handleStatusChange(row as JobVO)"
            />
          </template>
        </el-table-column>
        <el-table-column label="允许并发" width="90" align="center">
          <template #default="{ row }">
            <el-tag
              :type="(row as JobVO).concurrent === 1 ? 'success' : 'info'"
              size="small"
              disable-transitions
            >
              {{ (row as JobVO).concurrent === 1 ? '允许' : '禁止' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="备注" prop="description" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ (row as JobVO).description || '--' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime((row as JobVO).createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'monitor:job:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as JobVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'monitor:job:run'"
              link
              type="primary"
              :icon="CaretRight"
              @click="handleRun(row as JobVO)"
            >
              执行
            </el-button>
            <el-button
              v-permission="'monitor:job:query'"
              link
              type="primary"
              @click="logDrawerRef?.open(row as JobVO)"
            >
              日志
            </el-button>
            <el-button
              v-permission="'monitor:job:remove'"
              link
              type="danger"
              @click="handleDelete(row as JobVO)"
            >
              删除
            </el-button>
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
    </el-card>

    <!-- 新增 / 编辑对话框与执行日志抽屉 -->
    <JobFormDialog ref="formDialogRef" @success="fetchData" />
    <JobLogDrawer ref="logDrawerRef" />
  </div>
</template>

<style lang="scss" scoped>
.cron-text {
  font-family: var(--el-font-family-monospace, monospace);
}

.table-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.table-pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
