<script setup lang="ts">
/**
 * 参数配置：参数列表支持关键字（参数名称/键名）搜索、新增/编辑、
 * 删除（内置参数禁用删除，后端同样拒绝）。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Edit, Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { ConfigVO } from '@/api/system/config'
import { deleteConfigApi, getConfigPageApi } from '@/api/system/config'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'
import ConfigFormDialog from './components/ConfigFormDialog.vue'

defineOptions({ name: 'Config' })

/** 参数分页查询条件 */
interface ConfigQuery {
  keyword: string
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
} = usePage<ConfigQuery, ConfigVO>({
  fetcher: getConfigPageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof ConfigFormDialog>>()

/** 删除参数（内置参数按钮禁用，此处仍防御性确认后提交） */
async function handleDelete(row: ConfigVO): Promise<void> {
  if (row.builtIn) {
    return
  }
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除参数「${row.configName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteConfigApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}
</script>

<template>
  <div class="config-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="参数名称 / 参数键名"
            clearable
            style="width: 220px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">搜索</el-button>
          <el-button :icon="Refresh" @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 操作栏 -->
      <div class="table-toolbar">
        <el-button
          v-permission="'system:config:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 参数表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="参数名称" prop="configName" min-width="140" show-overflow-tooltip />
        <el-table-column label="参数键名" prop="configKey" min-width="180" show-overflow-tooltip />
        <el-table-column
          label="参数键值"
          prop="configValue"
          min-width="160"
          show-overflow-tooltip
        />
        <el-table-column label="是否内置" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.builtIn ? 'primary' : 'info'" size="small">
              {{ row.builtIn ? '内置' : '非内置' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="备注" prop="description" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.description || '--' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:config:edit'"
              link
              type="primary"
              :icon="Edit"
              @click="formDialogRef?.open('edit', row as ConfigVO)"
            >
              编辑
            </el-button>
            <el-tooltip
              v-permission="'system:config:remove'"
              :content="row.builtIn ? '内置参数不可删除' : ''"
              :disabled="!row.builtIn"
              placement="top"
            >
              <span class="delete-wrapper">
                <el-button
                  link
                  type="danger"
                  :disabled="Boolean(row.builtIn)"
                  @click="handleDelete(row as ConfigVO)"
                >
                  删除
                </el-button>
              </span>
            </el-tooltip>
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

    <!-- 新增 / 编辑对话框 -->
    <ConfigFormDialog ref="formDialogRef" @success="fetchData" />
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

.delete-wrapper {
  margin-left: 12px;
}
</style>
