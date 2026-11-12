<script setup lang="ts">
/**
 * 在线会话：账号 / IP 搜索，分页列表与强制下线（吊销 refreshToken，需对应按钮权限）。
 *
 * 一条活跃 refreshToken 记录代表一个在线会话，列表行 id 即会话记录 ID，作为 revoke 的 tokenId。
 * 菜单种子尚未登记该菜单，建议后端菜单 component 使用 `monitor/online/index`。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import type { OnlineUserVO } from '@/api/monitor/online'
import { forceLogoutApi, getOnlineUserPageApi } from '@/api/monitor/online'
import { usePage } from '@/composables/usePage'
import { formatDateTime } from '@/utils/format'

defineOptions({ name: 'OnlineUser' })

/** 在线会话分页查询条件 */
interface OnlineUserQuery {
  userName: string
  ip: string
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
} = usePage<OnlineUserQuery, OnlineUserVO>({
  fetcher: getOnlineUserPageApi,
  getDefaultQuery: () => ({ userName: '', ip: '' }),
})

/** 强制下线（高危操作，二次确认；当前页删空时自动回退一页） */
async function handleForceLogout(row: OnlineUserVO): Promise<void> {
  const confirmed = await ElMessageBox.confirm(
    `是否确认强制下线账号「${row.userName}」的该会话？下线后该会话将无法继续刷新令牌。`,
    '强制下线确认',
    { type: 'warning', confirmButtonText: '确 定', cancelButtonText: '取 消' },
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) {
    return
  }
  await forceLogoutApi(row.id)
  ElMessage.success('强制下线成功')
  if (items.value.length === 1 && pageIndex.value > 1) {
    pageIndex.value -= 1
  }
  await fetchData()
}
</script>

<template>
  <div class="online-page">
    <el-card shadow="never">
      <!-- 搜索表单 -->
      <el-form :model="query" inline @submit.prevent="handleSearch">
        <el-form-item label="登录账号">
          <el-input
            v-model="query.userName"
            placeholder="请输入登录账号"
            clearable
            style="width: 180px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item label="登录 IP">
          <el-input
            v-model="query.ip"
            placeholder="请输入登录 IP"
            clearable
            style="width: 180px"
            @keyup.enter="handleSearch"
            @clear="handleSearch"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">搜索</el-button>
          <el-button :icon="Refresh" @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 在线会话表格 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        :header-cell-style="{ background: 'var(--el-fill-color-light)' }"
      >
        <el-table-column label="登录账号" prop="userName" min-width="120" show-overflow-tooltip />
        <el-table-column label="昵称" min-width="110" show-overflow-tooltip>
          <template #default="{ row }">{{ (row as OnlineUserVO).nickName || '--' }}</template>
        </el-table-column>
        <el-table-column label="部门" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ (row as OnlineUserVO).deptName || '--' }}</template>
        </el-table-column>
        <el-table-column label="IP" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ (row as OnlineUserVO).ip || '--' }}</template>
        </el-table-column>
        <el-table-column
          label="User-Agent"
          prop="userAgent"
          min-width="220"
          show-overflow-tooltip
        />
        <el-table-column label="登录时间" width="170">
          <template #default="{ row }">
            {{ formatDateTime((row as OnlineUserVO).loginTime) }}
          </template>
        </el-table-column>
        <el-table-column label="过期时间" width="170">
          <template #default="{ row }">
            {{ formatDateTime((row as OnlineUserVO).expiresAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button
              v-permission="'monitor:online:forceLogout'"
              link
              type="danger"
              @click="handleForceLogout(row as OnlineUserVO)"
            >
              强制下线
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
  </div>
</template>

<style lang="scss" scoped>
.table-pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
