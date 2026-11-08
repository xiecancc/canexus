<script setup lang="ts">
/**
 * 字典数据维护页：按路由 query.dictType 维护指定字典类型的数据项。
 *
 * - 顶部展示当前字典类型并支持返回类型列表（常量路由，不随菜单下发）
 * - 支持关键字搜索、Tag 色预览、行内启停用（失败回滚）、新增/编辑/删除
 */
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Back, Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { DictDataVO } from '@/api/system/dict'
import { deleteDictDataApi, getDictDataPageApi, updateDictDataApi } from '@/api/system/dict'
import { usePage } from '@/composables/usePage'
import { usePermission } from '@/composables/usePermission'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { DICT_TAG_TYPE_MAP } from '@/composables/useDict'
import type { DictItem } from '@/composables/useDict'
import { formatDateTime } from '@/utils/format'
import DictDataFormDialog from './components/DictDataFormDialog.vue'

defineOptions({ name: 'DictData' })

const route = useRoute()
const router = useRouter()

const { hasPerm } = usePermission()

/** 当前维护的字典类型（来自路由 query） */
const dictType = computed(() => String(route.query.dictType ?? ''))

/** 字典数据分页查询条件 */
interface DictDataQuery {
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
} = usePage<DictDataQuery, DictDataVO>({
  fetcher: (params) => getDictDataPageApi({ ...params, dictType: dictType.value }),
  getDefaultQuery: () => ({ keyword: '' }),
  immediate: false,
})

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof DictDataFormDialog>>()

/** 返回字典类型列表 */
function handleBack(): void {
  void router.push('/system/dict')
}

/** 行内切换状态（契约 PUT 为全量更新，提交完整字段；失败由组合式回滚） */
async function handleStatusChange(row: DictDataVO): Promise<void> {
  const success = await commitStatusToggle(row, (status) =>
    updateDictDataApi(row.id, row.version, {
      dictType: row.dictType,
      dictLabel: row.dictLabel,
      dictValue: row.dictValue,
      tagType: row.tagType,
      description: row.description,
      orderNum: row.orderNum,
      status,
    }),
  )
  if (success) {
    ElMessage.success(row.status === 1 ? '已启用' : '已停用')
  }
}

/** 删除字典数据（确认后调用，当前页删空时自动回退一页） */
async function handleDelete(row: DictDataVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除字典数据「${row.dictLabel}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteDictDataApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

// 同页签切换不同字典类型（query 变化组件复用）时，重置条件并重新加载
watch(
  dictType,
  (value) => {
    if (value) {
      handleReset()
    }
  },
  { immediate: true },
)
</script>

<template>
  <div class="dict-data-page">
    <!-- 当前字典类型 -->
    <el-card shadow="never" class="data-header">
      <div class="header-bar">
        <el-button :icon="Back" @click="handleBack">返回</el-button>
        <span v-if="dictType" class="header-title">
          当前字典：<el-tag type="primary">{{ dictType }}</el-tag>
        </span>
        <span v-else class="header-title header-missing"
          >路由缺少 dictType 参数，请从字典类型列表进入</span
        >
      </div>
    </el-card>

    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="字典标签 / 字典键值"
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
          :disabled="!dictType"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 字典数据表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="字典标签" prop="dictLabel" min-width="120" show-overflow-tooltip />
        <el-table-column label="字典键值" prop="dictValue" min-width="120" show-overflow-tooltip />
        <el-table-column label="标签预览" width="120" align="center">
          <template #default="{ row }">
            <!-- 以该行自身作为字典项渲染，预览业务侧最终展示效果 -->
            <DictTag :items="[row as DictItem]" :value="row.dictValue" />
          </template>
        </el-table-column>
        <el-table-column label="排序" prop="orderNum" width="70" align="center" />
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              :disabled="!hasPerm('system:dict:edit')"
              @change="handleStatusChange(row as DictDataVO)"
            />
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
              v-permission="'system:dict:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as DictDataVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:dict:remove'"
              link
              type="danger"
              @click="handleDelete(row as DictDataVO)"
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
    <DictDataFormDialog ref="formDialogRef" :dict-type="dictType" @success="fetchData" />
  </div>
</template>

<style lang="scss" scoped>
.data-header {
  margin-bottom: 16px;
}

.header-bar {
  display: flex;
  align-items: center;
  gap: 16px;
}

.header-title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
}

.header-missing {
  color: var(--el-color-danger);
  font-weight: 400;
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
