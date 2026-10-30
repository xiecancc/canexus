<script setup lang="ts">
/**
 * 菜单管理：树表格展示菜单层级（目录/菜单/按钮），
 * 支持关键字/状态搜索、展开/收起切换、新增（顶级/子级）、编辑与删除。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search, Switch } from '@element-plus/icons-vue'
import type { MenuVO } from '@/api/system/menu'
import { deleteMenuApi, getMenuTreeApi } from '@/api/system/menu'
import {
  SYS_MENU_TYPE,
  SYS_NORMAL_DISABLE,
  SYS_VISIBLE,
  getDictLabel,
  getDictTagType,
} from '@/utils/dict'
import { formatDateTime } from '@/utils/format'
import MenuFormDialog from './components/MenuFormDialog.vue'

defineOptions({ name: 'Menu' })

/** 菜单树查询条件 */
interface MenuQuery {
  keyword: string
  status?: number
}

/** 菜单树数据 */
const menuTree = ref<MenuVO[]>([])

/** 加载中 */
const loading = ref(false)

/** 查询条件（树接口直接按条件过滤） */
const query = reactive<MenuQuery>({ keyword: '' })

/** 是否默认展开全部（切换时重建表格使配置生效） */
const isExpandAll = ref(true)

/** 表格重建开关（展开/收起切换时先卸载再挂载） */
const refreshTable = ref(true)

/** 表单对话框引用 */
const formDialogRef = ref<InstanceType<typeof MenuFormDialog>>()

/** 加载菜单树表格 */
async function loadMenuTree(): Promise<void> {
  loading.value = true
  try {
    menuTree.value = await getMenuTreeApi({
      keyword: query.keyword.trim() || undefined,
      status: query.status,
    })
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    loading.value = false
  }
}

/** 搜索：按关键字 / 状态查询 */
function handleSearch(): void {
  void loadMenuTree()
}

/** 重置搜索条件 */
function handleReset(): void {
  query.keyword = ''
  query.status = undefined
  handleSearch()
}

/** 切换展开 / 收起（通过重建表格应用 default-expand-all） */
function handleToggleExpand(): void {
  refreshTable.value = false
  isExpandAll.value = !isExpandAll.value
  void nextTick(() => {
    refreshTable.value = true
  })
}

/** 删除菜单（确认后调用） */
async function handleDelete(row: MenuVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除菜单「${row.menuName}」？其子级将一并受影响，删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteMenuApi(row.id)
  ElMessage.success('删除成功')
  await loadMenuTree()
}

loadMenuTree()
</script>

<template>
  <div class="menu-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="菜单名称 / 权限标识"
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
          v-permission="'system:menu:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
        <el-button :icon="Refresh" @click="loadMenuTree">刷新</el-button>
        <el-button :icon="Switch" @click="handleToggleExpand">
          {{ isExpandAll ? '收起' : '展开' }}
        </el-button>
      </div>

      <!-- 菜单树表格 -->
      <el-table
        v-if="refreshTable"
        v-loading="loading"
        :data="menuTree"
        row-key="id"
        :default-expand-all="isExpandAll"
        :tree-props="{ children: 'children' }"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="菜单名称" prop="menuName" min-width="160" show-overflow-tooltip />
        <el-table-column label="图标" width="70" align="center">
          <template #default="{ row }">
            <AppIcon v-if="row.icon" :name="row.icon" :size="16" />
            <span v-else>--</span>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="getDictTagType(SYS_MENU_TYPE, row.menuType)">
              {{ getDictLabel(SYS_MENU_TYPE, row.menuType) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="权限标识" prop="perms" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">{{ row.perms || '--' }}</template>
        </el-table-column>
        <el-table-column label="路由地址" prop="path" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.path || '--' }}</template>
        </el-table-column>
        <el-table-column label="组件路径" prop="component" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">{{ row.component || '--' }}</template>
        </el-table-column>
        <el-table-column label="排序" prop="orderNum" width="70" align="center" />
        <el-table-column label="可见" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="getDictTagType(SYS_VISIBLE, row.visible)">
              {{ getDictLabel(SYS_VISIBLE, row.visible) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="getDictTagType(SYS_NORMAL_DISABLE, row.status)">
              {{ getDictLabel(SYS_NORMAL_DISABLE, row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="row.menuType !== 3"
              v-permission="'system:menu:add'"
              link
              type="primary"
              @click="formDialogRef?.open('create', undefined, row.id)"
            >
              新增子级
            </el-button>
            <el-button
              v-permission="'system:menu:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as MenuVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:menu:remove'"
              link
              type="danger"
              @click="handleDelete(row as MenuVO)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新增 / 编辑对话框 -->
    <MenuFormDialog ref="formDialogRef" @success="loadMenuTree" />
  </div>
</template>

<style lang="scss" scoped>
.table-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}
</style>
