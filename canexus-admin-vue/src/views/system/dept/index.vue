<script setup lang="ts">
/**
 * 部门管理：树表格展示部门层级，支持展开/收起切换、
 * 新增（顶级/子级）、编辑与删除。
 */
import { nextTick, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Switch } from '@element-plus/icons-vue'
import type { DeptVO } from '@/api/system/dept'
import { deleteDeptApi, getDeptTreeApi } from '@/api/system/dept'
import { SYS_NORMAL_DISABLE, getDictTagType } from '@/utils/dict'
import { formatDateTime } from '@/utils/format'
import DeptFormDialog from './components/DeptFormDialog.vue'

defineOptions({ name: 'Dept' })

/** 部门树数据 */
const deptTree = ref<DeptVO[]>([])

/** 加载中 */
const loading = ref(false)

/** 是否默认展开全部（切换时重建表格使配置生效） */
const isExpandAll = ref(true)

/** 表格重建开关（展开/收起切换时先卸载再挂载） */
const refreshTable = ref(true)

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof DeptFormDialog>>()

/** 加载部门树表格 */
async function loadDeptTree(): Promise<void> {
  loading.value = true
  try {
    deptTree.value = await getDeptTreeApi()
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    loading.value = false
  }
}

/** 切换展开 / 收起（通过重建表格应用 default-expand-all） */
function handleToggleExpand(): void {
  refreshTable.value = false
  isExpandAll.value = !isExpandAll.value
  void nextTick(() => {
    refreshTable.value = true
  })
}

/** 删除部门（确认后调用） */
async function handleDelete(row: DeptVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除部门「${row.deptName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteDeptApi(row.id)
  ElMessage.success('删除成功')
  await loadDeptTree()
}

loadDeptTree()
</script>

<template>
  <div class="dept-page">
    <el-card shadow="never">
      <!-- 操作栏 -->
      <div class="table-toolbar">
        <el-button
          v-permission="'system:dept:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
        <el-button :icon="Refresh" @click="loadDeptTree">刷新</el-button>
        <el-button :icon="Switch" @click="handleToggleExpand">
          {{ isExpandAll ? '收起' : '展开' }}
        </el-button>
      </div>

      <!-- 部门树表格 -->
      <el-table
        v-if="refreshTable"
        v-loading="loading"
        :data="deptTree"
        row-key="id"
        :default-expand-all="isExpandAll"
        :tree-props="{ children: 'children' }"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="部门名称" prop="deptName" min-width="220" show-overflow-tooltip />
        <el-table-column label="排序" prop="orderNum" width="80" align="center" />
        <el-table-column label="负责人" width="120">
          <template #default="{ row }">{{ row.leader || '--' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="getDictTagType(SYS_NORMAL_DISABLE, row.status)">
              {{ row.status === 1 ? '正常' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="180">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:dept:add'"
              link
              type="primary"
              @click="formDialogRef?.open('create', undefined, row.id)"
            >
              新增子级
            </el-button>
            <el-button
              v-permission="'system:dept:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as DeptVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:dept:remove'"
              link
              type="danger"
              @click="handleDelete(row as DeptVO)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新增 / 编辑对话框 -->
    <DeptFormDialog ref="formDialogRef" :dept-tree="deptTree" @success="loadDeptTree" />
  </div>
</template>

<style lang="scss" scoped>
.table-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}
</style>
