<script setup lang="ts">
/**
 * 用户管理：左侧部门树（点击过滤右侧列表，支持关键字搜索与刷新），
 * 右侧用户列表支持关键字/状态搜索、行内启停用（失败回滚）、
 * 新增/编辑、分配角色、重置密码、Excel 导入导出与删除。
 */
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { TreeInstance, TreeNodeData } from 'element-plus'
import { ArrowDown, Download, Plus, Refresh, Search, Upload } from '@element-plus/icons-vue'
import type { DeptVO } from '@/api/system/dept'
import { getDeptTreeApi } from '@/api/system/dept'
import type { UserVO } from '@/api/system/user'
import { deleteUserApi, getUserPageApi, updateUserStatusApi } from '@/api/system/user'
import { usePage } from '@/composables/usePage'
import { usePermission } from '@/composables/usePermission'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { downloadFile } from '@/utils/download'
import { formatDateTime } from '@/utils/format'
import UserFormDialog from './components/UserFormDialog.vue'
import UserImportDialog from './components/UserImportDialog.vue'
import UserPwdDialog from './components/UserPwdDialog.vue'
import UserRoleDialog from './components/UserRoleDialog.vue'

defineOptions({ name: 'User' })

const { hasPerm } = usePermission()

/* ==================== 左侧部门树 ==================== */

/** 部门树数据 */
const deptTree = ref<DeptVO[]>([])

/** 部门树加载中 */
const deptLoading = ref(false)

/** 部门树搜索关键字 */
const deptKeyword = ref('')

/** 部门树组件引用 */
const deptTreeRef = ref<TreeInstance>()

/** 加载部门树 */
async function loadDeptTree(): Promise<void> {
  deptLoading.value = true
  try {
    deptTree.value = await getDeptTreeApi()
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    deptLoading.value = false
  }
}

/** 部门树节点过滤（配合搜索关键字） */
function filterDeptNode(value: string, data: TreeNodeData): boolean {
  if (!value) {
    return true
  }
  return String(data.deptName ?? '').includes(value)
}

// 关键字变化时触发树过滤
watch(deptKeyword, (value) => {
  deptTreeRef.value?.filter(value)
})

/* ==================== 右侧用户列表 ==================== */

/** 用户分页查询条件 */
interface UserQuery {
  keyword: string
  deptId?: string
  status?: number
}

const {
  loading: tableLoading,
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
} = usePage<UserQuery, UserVO>({
  fetcher: getUserPageApi,
  getDefaultQuery: () => ({ keyword: '' }),
})

/** 部门树节点点击：按部门过滤用户列表（重复点击同一部门忽略） */
function handleDeptNodeClick(data: DeptVO): void {
  if (query.deptId === data.id) {
    return
  }
  query.deptId = data.id
  handleSearch()
}

/** 重置查询：恢复默认条件并清除部门树选中高亮 */
function handleQueryReset(): void {
  handleReset()
  deptTreeRef.value?.setCurrentKey(null as unknown as string)
}

/** 行内切换状态：成功轻提示，失败由组合式回滚（错误提示在请求层） */
async function handleStatusChange(row: UserVO): Promise<void> {
  const success = await commitStatusToggle(row, (status) => updateUserStatusApi(row.id, status))
  if (success) {
    ElMessage.success(row.status === 1 ? '已启用' : '已停用')
  }
}

/** 删除用户（确认后调用，当前页删空时自动回退一页） */
async function handleDelete(row: UserVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除用户「${row.userName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteUserApi(row.id)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/* ==================== 导入导出 ==================== */

/** 导出中（按钮 loading） */
const exportLoading = ref(false)

/** 按当前搜索条件导出用户数据（keyword / deptId / status 透传，空条件不拼接） */
async function handleExport(): Promise<void> {
  exportLoading.value = true
  try {
    await downloadFile(
      '/system/user/export',
      {
        keyword: query.keyword || undefined,
        deptId: query.deptId,
        status: query.status,
      },
      '用户数据.xlsx',
    )
    ElMessage.success('导出成功')
  } catch {
    // 错误提示由下载层统一处理
  } finally {
    exportLoading.value = false
  }
}

/* ==================== 对话框 ==================== */

const formDialogRef = ref<InstanceType<typeof UserFormDialog>>()
const roleDialogRef = ref<InstanceType<typeof UserRoleDialog>>()
const pwdDialogRef = ref<InstanceType<typeof UserPwdDialog>>()
const importDialogRef = ref<InstanceType<typeof UserImportDialog>>()

/** 更多操作下拉指令分发 */
function handleMoreCommand(command: string, row: UserVO): void {
  if (command === 'role') {
    roleDialogRef.value?.open(row)
  } else if (command === 'pwd') {
    pwdDialogRef.value?.open(row)
  } else if (command === 'delete') {
    void handleDelete(row)
  }
}

/** 是否拥有"更多"下拉任一操作权限（避免渲染空菜单） */
const hasMorePerm = computed(() =>
  hasPerm(['system:user:edit', 'system:user:resetPwd', 'system:user:remove']),
)

/** 是否允许操作目标行（超管 admin 行禁用，后端同样拒绝） */
function isRowLocked(row: UserVO): boolean {
  return row.userName === 'admin'
}

loadDeptTree()
</script>

<template>
  <div class="user-page">
    <el-row :gutter="16">
      <!-- 左侧部门树 -->
      <el-col :xs="24" :sm="8" :md="6" :lg="5" :xl="4">
        <el-card v-loading="deptLoading" shadow="never" class="dept-card">
          <template #header>
            <div class="panel-header">
              <span>部门列表</span>
              <el-tooltip content="刷新部门树" placement="top">
                <el-button link type="primary" :icon="Refresh" @click="loadDeptTree" />
              </el-tooltip>
            </div>
          </template>
          <el-input
            v-model="deptKeyword"
            placeholder="搜索部门"
            clearable
            :prefix-icon="Search"
            class="dept-search"
          />
          <el-tree
            ref="deptTreeRef"
            class="dept-tree"
            :data="deptTree"
            :props="{ label: 'deptName', children: 'children' }"
            node-key="id"
            default-expand-all
            highlight-current
            :expand-on-click-node="false"
            :filter-node-method="filterDeptNode"
            @node-click="handleDeptNodeClick"
          />
        </el-card>
      </el-col>

      <!-- 右侧用户列表 -->
      <el-col :xs="24" :sm="16" :md="18" :lg="19" :xl="20">
        <el-card shadow="never">
          <!-- 搜索表单 -->
          <el-form :model="query" inline @submit.prevent="handleSearch">
            <el-form-item label="关键字">
              <el-input
                v-model="query.keyword"
                placeholder="用户名 / 昵称 / 手机号"
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
              <el-button :icon="Refresh" @click="handleQueryReset">重置</el-button>
            </el-form-item>
          </el-form>

          <!-- 操作栏 -->
          <div class="table-toolbar">
            <el-button
              v-permission="'system:user:add'"
              type="primary"
              :icon="Plus"
              @click="formDialogRef?.open('create')"
            >
              新增
            </el-button>
            <el-button
              v-permission="'system:user:import'"
              type="info"
              :icon="Upload"
              @click="importDialogRef?.open()"
            >
              导入
            </el-button>
            <el-button
              v-permission="'system:user:export'"
              type="warning"
              :icon="Download"
              :loading="exportLoading"
              @click="handleExport"
            >
              导出
            </el-button>
          </div>

          <!-- 用户表格 -->
          <el-table
            v-loading="tableLoading"
            :data="items"
            row-key="id"
            :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
          >
            <el-table-column type="selection" width="46" />
            <el-table-column label="用户名" prop="userName" min-width="110" show-overflow-tooltip />
            <el-table-column label="昵称" prop="nickName" min-width="110" show-overflow-tooltip />
            <el-table-column label="部门" prop="deptName" min-width="130" show-overflow-tooltip />
            <el-table-column label="手机号" prop="phone" min-width="120" show-overflow-tooltip>
              <template #default="{ row }">{{ row.phone || '--' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="80" align="center">
              <template #default="{ row }">
                <el-switch
                  v-model="row.status"
                  :active-value="1"
                  :inactive-value="0"
                  :disabled="isRowLocked(row as UserVO) || !hasPerm('system:user:edit')"
                  @change="handleStatusChange(row as UserVO)"
                />
              </template>
            </el-table-column>
            <el-table-column label="角色" min-width="160">
              <template #default="{ row }">
                <template v-if="row.roles?.length">
                  <el-tag
                    v-for="role in row.roles"
                    :key="role.id"
                    size="small"
                    type="info"
                    class="role-tag"
                  >
                    {{ role.roleName }}
                  </el-tag>
                </template>
                <span v-else>--</span>
              </template>
            </el-table-column>
            <el-table-column label="创建时间" width="170">
              <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="200" fixed="right">
              <template #default="{ row }">
                <el-button
                  v-permission="'system:user:edit'"
                  link
                  type="primary"
                  :disabled="isRowLocked(row as UserVO)"
                  @click="formDialogRef?.open('edit', row as UserVO)"
                >
                  编辑
                </el-button>
                <el-dropdown
                  v-if="hasMorePerm"
                  :disabled="isRowLocked(row as UserVO)"
                  class="more-dropdown"
                  @command="(command: string) => handleMoreCommand(command, row as UserVO)"
                >
                  <el-button link type="primary" :disabled="isRowLocked(row as UserVO)">
                    更多
                    <el-icon class="el-icon--right"><ArrowDown /></el-icon>
                  </el-button>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <!-- el-dropdown-item 根节点为组件，v-permission 指令不生效，改用 hasPerm 控制 -->
                      <el-dropdown-item v-if="hasPerm('system:user:edit')" command="role">
                        分配角色
                      </el-dropdown-item>
                      <el-dropdown-item v-if="hasPerm('system:user:resetPwd')" command="pwd">
                        重置密码
                      </el-dropdown-item>
                      <el-dropdown-item
                        v-if="hasPerm('system:user:remove')"
                        command="delete"
                        divided
                      >
                        删除
                      </el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
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
      </el-col>
    </el-row>

    <!-- 对话框 -->
    <UserFormDialog ref="formDialogRef" :dept-tree="deptTree" @success="fetchData" />
    <UserRoleDialog ref="roleDialogRef" @success="fetchData" />
    <UserPwdDialog ref="pwdDialogRef" />
    <UserImportDialog ref="importDialogRef" @success="fetchData" />
  </div>
</template>

<style lang="scss" scoped>
.user-page {
  .dept-card {
    height: 100%;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
}

.dept-search {
  margin-bottom: 8px;
}

.dept-tree {
  /* 高度与右侧表格区大致对齐，超出滚动 */
  max-height: calc(100vh - 320px);
  overflow-y: auto;
}

.table-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.role-tag {
  margin-right: 4px;
}

.more-dropdown {
  margin-left: 12px;
  vertical-align: middle;
}

.table-pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
