<script setup lang="ts">
/**
 * 角色管理：关键字/状态搜索、角色列表（数据范围、行内启停用）、
 * 新增/编辑（含菜单授权树）与删除。
 * 角色无独立状态接口：切换状态先取详情再整体更新，避免清空授权数据。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { RoleVO } from '@/api/system/role'
import { deleteRoleApi, getRoleApi, getRolePageApi, updateRoleApi } from '@/api/system/role'
import { usePage } from '@/composables/usePage'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { SYS_DATA_SCOPE, getDictLabel } from '@/utils/dict'
import { formatDateTime } from '@/utils/format'
import RoleFormDialog from './components/RoleFormDialog.vue'

defineOptions({ name: 'Role' })

/** 角色分页查询条件 */
interface RoleQuery {
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
} = usePage<RoleQuery, RoleVO>({
  fetcher: getRolePageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof RoleFormDialog>>()

/** 提交角色状态：先取详情（含 menuIds 与最新 version）再整体更新，防止清空菜单授权 */
async function submitRoleStatus(row: RoleVO, status: number): Promise<boolean> {
  const detail = await getRoleApi(row.id)
  return updateRoleApi(row.id, detail.version, {
    roleName: detail.roleName,
    roleKey: detail.roleKey,
    dataScope: detail.dataScope,
    status,
    description: detail.description,
    menuIds: detail.menuIds,
  })
}

/** 行内切换状态：成功轻提示，失败由组合式回滚（错误提示在请求层） */
async function handleStatusChange(row: RoleVO): Promise<void> {
  const success = await commitStatusToggle(row, (status) => submitRoleStatus(row, status))
  if (success) {
    ElMessage.success(row.status === 1 ? '已启用' : '已停用')
  }
}

/** 删除角色（确认后调用，当前页删空时自动回退一页） */
async function handleDelete(row: RoleVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除角色「${row.roleName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteRoleApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}
</script>

<template>
  <div class="role-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="角色名称 / 权限字符"
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
            <el-option label="正常" :value="1" />
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
          v-permission="'system:role:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 角色表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="角色名称" prop="roleName" min-width="140" show-overflow-tooltip />
        <el-table-column label="权限字符" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag type="primary">{{ row.roleKey }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="数据范围" min-width="160">
          <template #default="{ row }">{{ getDictLabel(SYS_DATA_SCOPE, row.dataScope) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              v-permission="'system:role:edit'"
              @change="handleStatusChange(row as RoleVO)"
            />
          </template>
        </el-table-column>
        <el-table-column label="备注" prop="description" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.description || '--' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:role:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as RoleVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:role:remove'"
              link
              type="danger"
              @click="handleDelete(row as RoleVO)"
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
    <RoleFormDialog ref="formDialogRef" @success="fetchData" />
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
