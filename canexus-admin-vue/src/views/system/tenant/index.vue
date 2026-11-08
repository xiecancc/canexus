<script setup lang="ts">
/**
 * 租户管理：关键字 / 状态搜索，行内启停用（契约无独立状态接口，
 * 经全量更新提交，失败回滚）、新增 / 编辑（编辑禁用租户标识）与删除。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { TenantVO } from '@/api/system/tenant'
import { deleteTenantApi, getTenantPageApi, updateTenantApi } from '@/api/system/tenant'
import { usePage } from '@/composables/usePage'
import { usePermission } from '@/composables/usePermission'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { formatDateTime } from '@/utils/format'
import TenantFormDialog from './components/TenantFormDialog.vue'

defineOptions({ name: 'Tenant' })

const { hasPerm } = usePermission()

/** 租户分页查询条件 */
interface TenantQuery {
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
} = usePage<TenantQuery, TenantVO>({
  fetcher: getTenantPageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 行内切换状态：契约无独立状态接口，用行数据全量更新提交，失败由组合式回滚 */
async function handleStatusChange(row: TenantVO): Promise<void> {
  const success = await commitStatusToggle(row, (status) =>
    updateTenantApi(row.id, row.version, {
      tenantKey: row.tenantKey,
      tenantName: row.tenantName,
      contactPhone: row.contactPhone,
      status,
      description: row.description,
      expireTime: row.expireTime ?? undefined,
    }),
  )
  if (success) {
    ElMessage.success(row.status === 1 ? '已启用' : '已停用')
  }
}

/** 删除租户（当前页删空时自动回退一页） */
async function handleDelete(row: TenantVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除租户「${row.tenantName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteTenantApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/** 新增 / 编辑对话框引用 */
const formDialogRef = ref<InstanceType<typeof TenantFormDialog>>()
</script>

<template>
  <div class="tenant-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="租户标识 / 租户名称"
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
            <el-option label="启用" :value="1" />
            <el-option label="停用" :value="0" />
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
          v-permission="'system:tenant:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 租户表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="租户标识" prop="tenantKey" min-width="120" show-overflow-tooltip />
        <el-table-column label="租户名称" prop="tenantName" min-width="160" show-overflow-tooltip />
        <el-table-column label="联系电话" prop="contactPhone" min-width="130">
          <template #default="{ row }">{{ (row as TenantVO).contactPhone || '--' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              :disabled="!hasPerm('system:tenant:edit')"
              @change="handleStatusChange(row as TenantVO)"
            />
          </template>
        </el-table-column>
        <el-table-column label="过期时间" width="170">
          <template #default="{ row }">{{
            formatDateTime((row as TenantVO).expireTime) || '--'
          }}</template>
        </el-table-column>
        <el-table-column label="备注" prop="description" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ (row as TenantVO).description || '--' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime((row as TenantVO).createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:tenant:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as TenantVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:tenant:remove'"
              link
              type="danger"
              @click="handleDelete(row as TenantVO)"
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

    <!-- 新增 / 编辑对话框 -->
    <TenantFormDialog ref="formDialogRef" @success="fetchData" />
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
