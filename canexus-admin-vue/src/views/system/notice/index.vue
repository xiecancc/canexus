<script setup lang="ts">
/**
 * 通知公告：标题 / 类型 / 状态搜索，行内启停（契约无独立状态接口，
 * 经全量更新提交，失败回滚）、查看全文抽屉、新增 / 编辑与删除。
 *
 * 类型字典经 useNoticeTypeDict() 从 useDict('sys_notice_type') 动态获取（本地常量兜底）。
 */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search, View } from '@element-plus/icons-vue'
import type { NoticeVO } from '@/api/system/notice'
import {
  deleteNoticeApi,
  getNoticeApi,
  getNoticePageApi,
  updateNoticeApi,
} from '@/api/system/notice'
import { usePage } from '@/composables/usePage'
import { usePermission } from '@/composables/usePermission'
import { commitStatusToggle } from '@/composables/useStatusToggle'
import { formatDateTime } from '@/utils/format'
import { getDictTagType, getDictLabel, useNoticeTypeDict } from '@/utils/dict'
import NoticeDetailDrawer from './components/NoticeDetailDrawer.vue'
import NoticeFormDialog from './components/NoticeFormDialog.vue'

defineOptions({ name: 'Notice' })

const { hasPerm } = usePermission()

/** 公告类型字典（useDict('sys_notice_type')，未拉取到时回退本地常量） */
const noticeTypeItems = useNoticeTypeDict()

/** 通知公告分页查询条件 */
interface NoticeQuery {
  title: string
  noticeType?: number
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
} = usePage<NoticeQuery, NoticeVO>({
  fetcher: getNoticePageApi,
  getDefaultQuery: () => ({ title: '' }),
})

/** 行内切换状态：列表 VO 不含正文，先拉取详情再全量更新提交，失败由组合式回滚 */
async function handleStatusChange(row: NoticeVO): Promise<void> {
  const success = await commitStatusToggle(row, async (status) => {
    const detail = await getNoticeApi(row.id)
    return updateNoticeApi(row.id, {
      title: detail.title,
      noticeType: detail.noticeType,
      content: detail.content,
      status,
      description: detail.description,
    })
  })
  if (success) {
    ElMessage.success(row.status === 1 ? '已开启' : '已关闭')
  }
}

/** 删除通知公告（当前页删空时自动回退一页） */
async function handleDelete(row: NoticeVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认删除公告「${row.title}」？删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await deleteNoticeApi(row.id)
  ElMessage.success('删除成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}

/** 查看抽屉与新增 / 编辑对话框引用 */
const detailDrawerRef = ref<InstanceType<typeof NoticeDetailDrawer>>()
const formDialogRef = ref<InstanceType<typeof NoticeFormDialog>>()
</script>

<template>
  <div class="notice-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="公告标题">
          <el-input
            v-model="query.title"
            placeholder="请输入公告标题"
            clearable
            style="width: 200px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="类型">
          <el-select
            v-model="query.noticeType"
            placeholder="全部"
            clearable
            style="width: 140px"
            @change="handleSearch"
          >
            <el-option
              v-for="item in noticeTypeItems"
              :key="item.value"
              :label="item.label"
              :value="Number(item.value)"
            />
          </el-select>
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
            <el-option label="关闭" :value="0" />
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
          v-permission="'system:notice:add'"
          type="primary"
          :icon="Plus"
          @click="formDialogRef?.open('create')"
        >
          新增
        </el-button>
      </div>

      <!-- 公告表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="公告标题" prop="title" min-width="200" show-overflow-tooltip />
        <el-table-column label="类型" width="90" align="center">
          <template #default="{ row }">
            <el-tag
              :type="getDictTagType(noticeTypeItems, (row as NoticeVO).noticeType)"
              size="small"
              disable-transitions
            >
              {{ getDictLabel(noticeTypeItems, (row as NoticeVO).noticeType, '--') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              :disabled="!hasPerm('system:notice:edit')"
              @change="handleStatusChange(row as NoticeVO)"
            />
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime((row as NoticeVO).createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="170" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'system:notice:query'"
              link
              type="primary"
              :icon="View"
              @click="detailDrawerRef?.open(row as NoticeVO)"
            >
              查看
            </el-button>
            <el-button
              v-permission="'system:notice:edit'"
              link
              type="primary"
              @click="formDialogRef?.open('edit', row as NoticeVO)"
            >
              编辑
            </el-button>
            <el-button
              v-permission="'system:notice:remove'"
              link
              type="danger"
              @click="handleDelete(row as NoticeVO)"
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

    <!-- 查看抽屉与新增 / 编辑对话框 -->
    <NoticeDetailDrawer ref="detailDrawerRef" />
    <NoticeFormDialog ref="formDialogRef" @success="fetchData" />
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
