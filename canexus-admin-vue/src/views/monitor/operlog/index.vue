<script setup lang="ts">
/**
 * 操作日志：支持模块标题/操作人/业务类型（字典 sys_oper_type）/状态/日期范围搜索，
 * 详情抽屉、单条删除与清空全部（均需对应按钮权限）。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Refresh, Search, View } from '@element-plus/icons-vue'
import type { OperLogVO } from '@/api/monitor/operlog'
import { cleanOperLogApi, deleteOperLogApi, getOperLogPageApi } from '@/api/monitor/operlog'
import { useDict } from '@/composables/useDict'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'
import OperLogDetailDrawer from './components/OperLogDetailDrawer.vue'

defineOptions({ name: 'OperLog' })

/** 业务类型字典（sys_oper_type，后端字典种子提供） */
const { items: operTypeItems } = useDict('sys_oper_type')

/** 操作日志分页查询条件 */
interface OperLogQuery {
  title: string
  operUserName: string
  businessType?: string
  status?: number
  beginTime?: string
  endTime?: string
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
} = usePage<OperLogQuery, OperLogVO>({
  fetcher: getOperLogPageApi,
  getDefaultQuery: () => ({ title: '', operUserName: '' }),
})

/** 日期范围（el-date-picker daterange，value-format 为 YYYY-MM-DD） */
const dateRange = ref<[string, string] | null>(null)

/** 详情抽屉引用 */
const detailDrawerRef = ref<InstanceType<typeof OperLogDetailDrawer>>()

/** 搜索：合并日期范围到查询条件 */
function handleQuerySearch(): void {
  query.beginTime = dateRange.value?.[0]
  query.endTime = dateRange.value?.[1]
  handleSearch()
}

/** 重置：清空日期范围并恢复默认条件 */
function handleQueryReset(): void {
  dateRange.value = null
  handleReset()
}

/** 删除单条日志（当前页删空时自动回退一页） */
async function handleDelete(row: OperLogVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除「${row.title}」的这条操作日志？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteOperLogApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/** 按时间范围清空操作日志（危险操作：须先选定日期范围、确认跨度并二次确认） */
async function handleClean(): Promise<void> {
  if (!dateRange.value) {
    ElMessage.warning('请先在搜索条件中选择要清空的起止日期')
    return
  }
  const [beginTime, endTime] = dateRange.value
  const spanDays = (Date.parse(endTime) - Date.parse(beginTime)) / 86_400_000
  if (spanDays > 30) {
    ElMessage.warning('单次清空的时间范围不得超过 31 天')
    return
  }
  const confirmed = await ElMessageBox.confirm(
    `是否确认清空 ${beginTime} 至 ${endTime} 的操作日志？清空后不可恢复！`,
    '清空确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await cleanOperLogApi({ beginTime, endTime, confirm: true })
  ElMessage.success('清空成功')
  await fetchData()
}
</script>

<template>
  <div class="operlog-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleQuerySearch">
        <el-form-item label="模块标题">
          <el-input
            v-model="query.title"
            placeholder="请输入模块标题"
            clearable
            style="width: 180px"
            @keyup.enter="handleQuerySearch"
            @clear="handleQuerySearch"
          />
        </el-form-item>
        <el-form-item label="操作人">
          <el-input
            v-model="query.operUserName"
            placeholder="请输入操作人员"
            clearable
            style="width: 160px"
            @keyup.enter="handleQuerySearch"
            @clear="handleQuerySearch"
          />
        </el-form-item>
        <el-form-item label="业务类型">
          <el-select
            v-model="query.businessType"
            placeholder="全部"
            clearable
            style="width: 150px"
            @change="handleQuerySearch"
          >
            <el-option
              v-for="item in operTypeItems"
              :key="item.dictValue"
              :label="item.dictLabel"
              :value="item.dictValue"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select
            v-model="query.status"
            placeholder="全部"
            clearable
            style="width: 130px"
            @change="handleQuerySearch"
          >
            <el-option label="成功" :value="1" />
            <el-option label="失败" :value="0" />
          </el-select>
        </el-form-item>
        <el-form-item label="操作时间">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            range-separator="-"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            value-format="YYYY-MM-DD"
            style="width: 240px"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleQuerySearch">搜索</el-button>
          <el-button :icon="Refresh" @click="handleQueryReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 操作栏 -->
      <div class="table-toolbar">
        <el-button
          v-permission="'monitor:operlog:clean'"
          type="danger"
          :icon="Delete"
          @click="handleClean"
        >
          清空
        </el-button>
      </div>

      <!-- 日志表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="模块" prop="title" min-width="120" show-overflow-tooltip />
        <el-table-column label="类型" width="90" align="center">
          <template #default="{ row }">
            <DictTag dict-type="sys_oper_type" :value="row.businessType" />
          </template>
        </el-table-column>
        <el-table-column label="请求方式" prop="requestMethod" width="90" align="center" />
        <el-table-column label="操作人" prop="operUserName" min-width="100" show-overflow-tooltip>
          <template #default="{ row }">{{ row.operUserName || '--' }}</template>
        </el-table-column>
        <el-table-column label="IP" prop="operIp" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.operIp || '--' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'danger'" size="small">
              {{ row.status === 1 ? '成功' : '失败' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="90" align="right">
          <template #default="{ row }">
            {{ row.costMs != null ? `${row.costMs} ms` : '--' }}
          </template>
        </el-table-column>
        <el-table-column label="操作时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.operTime) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'monitor:operlog:query'"
              link
              type="primary"
              :icon="View"
              @click="detailDrawerRef?.open(row as OperLogVO)"
            >
              详情
            </el-button>
            <el-button
              v-permission="'monitor:operlog:remove'"
              link
              type="danger"
              @click="handleDelete(row as OperLogVO)"
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

    <!-- 详情抽屉 -->
    <OperLogDetailDrawer ref="detailDrawerRef" />
  </div>
</template>

<style lang="scss" scoped>
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
