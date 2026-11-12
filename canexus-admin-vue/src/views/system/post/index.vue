<script setup lang="ts">
/**
 * 岗位管理：名称 / 编码 / 状态搜索，新增、编辑与删除（均需对应按钮权限）。
 *
 * 与后端菜单种子 component `system/post/index` 对应；编辑回显使用列表行数据，
 * 乐观锁 version 由列表响应携带并回传至更新 / 删除 URL。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import type { PostVO } from '@/api/system/post'
import { deletePostApi, getPostPageApi } from '@/api/system/post'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'
import PostFormDialog from './components/PostFormDialog.vue'

defineOptions({ name: 'Post' })

/** 岗位分页查询条件 */
interface PostQuery {
  postName: string
  postCode: string
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
} = usePage<PostQuery, PostVO>({
  fetcher: getPostPageApi,
  getDefaultQuery: () => ({ postName: '', postCode: '' }),
})

/** 新增 / 编辑对话框引用 */
const formDialogRef = ref<InstanceType<typeof PostFormDialog>>()

/** 删除岗位（当前页删空时自动回退一页；被用户引用时由后端拒绝并统一提示） */
async function handleDelete(row: PostVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除岗位「${row.postName}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deletePostApi(row.id, row.version)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}
</script>

<template>
  <div class="post-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="岗位名称">
          <el-input
            v-model="query.postName"
            placeholder="请输入岗位名称"
            clearable
            style="width: 180px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="岗位编码">
          <el-input
            v-model="query.postCode"
            placeholder="请输入岗位编码"
            clearable
            style="width: 180px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select
            v-model="query.status"
            placeholder="全部"
            clearable
            style="width: 130px"
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
          v-permission="'system:post:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 岗位表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="岗位名称" prop="postName" min-width="140" show-overflow-tooltip />
        <el-table-column label="岗位编码" prop="postCode" min-width="140" show-overflow-tooltip />
        <el-table-column label="显示顺序" prop="orderNum" width="100" align="center" />
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="(row as PostVO).status === 1 ? 'success' : 'info'" size="small">
              {{ (row as PostVO).status === 1 ? '启用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime((row as PostVO).createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:post:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as PostVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:post:remove'"
              link
              type="danger"
              @click="handleDelete(row as PostVO)"
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
    <PostFormDialog ref="formDialogRef" @success="fetchData" />
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
