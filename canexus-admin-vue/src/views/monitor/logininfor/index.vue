<script setup lang="ts">
/**
 * 登录日志：支持用户名/IP/状态/日期范围搜索、单条删除与清空全部（均需对应按钮权限）。
 * 组件目录名为 logininfor，与后端菜单种子 component 路径保持一致。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Refresh, Search } from '@element-plus/icons-vue'
import type { LoginLogVO } from '@/api/monitor/loginlog'
import { cleanLoginLogApi, deleteLoginLogApi, getLoginLogPageApi } from '@/api/monitor/loginlog'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'

defineOptions({ name: 'LoginLog' })

/** 登录日志分页查询条件 */
interface LoginLogQuery {
  userName: string
  ip: string
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
} = usePage<LoginLogQuery, LoginLogVO>({
  fetcher: getLoginLogPageApi,
  getDefaultQuery: () => ({ userName: '', ip: '' }),
})

/** 日期范围（el-date-picker daterange，value-format 为 YYYY-MM-DD） */
const dateRange = ref<[string, string] | null>(null)

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
async function handleDelete(row: LoginLogVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除账号「${row.userName}」的这条登录日志？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteLoginLogApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/** 按时间范围清空登录日志（危险操作：须先选定日期范围、确认跨度并二次确认） */
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
    `是否确认清空 ${beginTime} 至 ${endTime} 的登录日志？清空后不可恢复！`,
    '清空确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await cleanLoginLogApi({ beginTime, endTime, confirm: true })
  ElMessage.success('清空成功')
  await fetchData()
}
</script>

<template>
  <div class="loginlog-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleQuerySearch">
        <el-form-item label="用户名">
          <el-input
            v-model="query.userName"
            placeholder="请输入登录账号"
            clearable
            style="width: 180px"
            @keyup.enter="handleQuerySearch"
            @clear="handleQuerySearch"
          />
        </el-form-item>
        <el-form-item label="IP">
          <el-input
            v-model="query.ip"
            placeholder="请输入登录 IP"
            clearable
            style="width: 160px"
            @keyup.enter="handleQuerySearch"
            @clear="handleQuerySearch"
          />
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
        <el-form-item label="登录时间">
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
          v-permission="'monitor:logininfor:clean'"
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
        <el-table-column label="用户名" prop="userName" min-width="110" show-overflow-tooltip />
        <el-table-column label="IP" prop="ip" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ row.ip || '--' }}</template>
        </el-table-column>
        <el-table-column
          label="登录地点"
          prop="loginLocation"
          min-width="130"
          show-overflow-tooltip
        >
          <template #default="{ row }">{{ row.loginLocation || '--' }}</template>
        </el-table-column>
        <el-table-column label="User-Agent" prop="userAgent" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">{{ row.userAgent || '--' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'danger'" size="small">
              {{ row.status === 1 ? '成功' : '失败' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="消息" prop="msg" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.msg || '--' }}</template>
        </el-table-column>
        <el-table-column label="登录时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.loginTime) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="80" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'monitor:logininfor:remove'"
              link
              type="danger"
              @click="handleDelete(row as LoginLogVO)"
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
