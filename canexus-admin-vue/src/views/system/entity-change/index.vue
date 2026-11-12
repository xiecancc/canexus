<script setup lang="ts">
/**
 * 实体变更日志：只读分页列表，支持实体类型名 / 实体 ID / 字段名过滤。
 *
 * 后端仅有分页查询端点（写入由聚合仓储更新链路自动完成），故本页不含任何写操作。
 * 菜单种子尚未登记该菜单，建议后端菜单 component 使用 `system/entity-change/index`。
 */
import { ElMessage } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import type { EntityChangeLogVO } from '@/api/system/entitychange'
import { getEntityChangeLogPageApi } from '@/api/system/entitychange'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'

defineOptions({ name: 'EntityChangeLog' })

/** 实体变更日志分页查询条件 */
interface EntityChangeLogQuery {
  entityName: string
  entityId: string
  fieldName: string
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
} = usePage<EntityChangeLogQuery, EntityChangeLogVO>({
  fetcher: getEntityChangeLogPageApi,
  getDefaultQuery: () => ({ entityName: '', entityId: '', fieldName: '' }),
})

/** 复制变更值到剪贴板（长文本便于排查） */
async function handleCopy(value: string): Promise<void> {
  if (!value) {
    return
  }
  try {
    await navigator.clipboard.writeText(value)
    ElMessage.success('已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择文本')
  }
}
</script>

<template>
  <div class="entity-change-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="实体类型">
          <el-input
            v-model="query.entityName"
            placeholder="如 SysUser"
            clearable
            style="width: 180px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="实体 ID">
          <el-input
            v-model="query.entityId"
            placeholder="GUID（精确匹配）"
            clearable
            style="width: 260px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="字段名">
          <el-input
            v-model="query.fieldName"
            placeholder="请输入字段名"
            clearable
            style="width: 160px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">搜索</el-button>
          <el-button :icon="Refresh" @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 变更日志表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="实体类型" prop="entityName" min-width="130" show-overflow-tooltip />
        <el-table-column label="实体 ID" prop="entityId" min-width="200" show-overflow-tooltip />
        <el-table-column label="字段名" prop="fieldName" min-width="130" show-overflow-tooltip />
        <el-table-column label="变更前" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <el-link
              v-if="(row as EntityChangeLogVO).oldValue"
              type="info"
              :underline="false"
              @click="handleCopy((row as EntityChangeLogVO).oldValue)"
            >
              {{ (row as EntityChangeLogVO).oldValue }}
            </el-link>
            <span v-else>--</span>
          </template>
        </el-table-column>
        <el-table-column label="变更后" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <el-link
              v-if="(row as EntityChangeLogVO).newValue"
              type="primary"
              :underline="false"
              @click="handleCopy((row as EntityChangeLogVO).newValue)"
            >
              {{ (row as EntityChangeLogVO).newValue }}
            </el-link>
            <span v-else>--</span>
          </template>
        </el-table-column>
        <el-table-column label="变更时间" width="170">
          <template #default="{ row }">
            {{ formatDateTime((row as EntityChangeLogVO).changedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="变更人" width="200" show-overflow-tooltip>
          <template #default="{ row }">
            {{ (row as EntityChangeLogVO).changedBy || '系统任务' }}
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
.table-pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
