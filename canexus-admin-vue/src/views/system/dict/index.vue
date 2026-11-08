<script setup lang="ts">
/**
 * 字典管理：字典类型列表，支持关键字搜索、新增/编辑（类型键不可改）、
 * 删除与跳转对应字典数据维护页（/system/dict/data?dictType=xxx）。
 */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Edit, Plus, Refresh, Search, Tickets } from '@element-plus/icons-vue'
import type { DictTypeVO } from '@/api/system/dict'
import { deleteDictTypeApi, getDictTypePageApi } from '@/api/system/dict'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'
import DictTypeFormDialog from './components/DictTypeFormDialog.vue'

defineOptions({ name: 'Dict' })

const router = useRouter()

/** 字典类型分页查询条件 */
interface DictTypeQuery {
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
} = usePage<DictTypeQuery, DictTypeVO>({
  fetcher: getDictTypePageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof DictTypeFormDialog>>()

/** 跳转字典数据维护页 */
function handleDictData(row: DictTypeVO): void {
  void router.push({ path: '/system/dict/data', query: { dictType: row.dictType } })
}

/** 删除字典类型（存在字典数据时后端拒绝，提示由请求层统一处理） */
async function handleDelete(row: DictTypeVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除字典类型「${row.dictName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteDictTypeApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}
</script>

<template>
  <div class="dict-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="字典名称 / 字典类型"
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
          v-permission="'system:dict:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 字典类型表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="字典名称" prop="dictName" min-width="140" show-overflow-tooltip />
        <el-table-column label="字典类型" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <el-link type="primary" @click="handleDictData(row as DictTypeVO)">
              {{ row.dictType }}
            </el-link>
          </template>
        </el-table-column>
        <el-table-column label="备注" prop="description" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">{{ row.description || '--' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:dict:query'"
              link
              type="primary"
              :icon="Tickets"
              @click="handleDictData(row as DictTypeVO)"
            >
              字典数据
            </el-button>
            <el-button
              v-permission="'system:dict:edit'"
              link
              type="primary"
              :icon="Edit"
              @click="formDialogRef?.open('edit', row as DictTypeVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:dict:remove'"
              link
              type="danger"
              @click="handleDelete(row as DictTypeVO)"
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
    <DictTypeFormDialog ref="formDialogRef" @success="fetchData" />
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
