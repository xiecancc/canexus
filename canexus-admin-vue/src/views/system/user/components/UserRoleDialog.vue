<script setup lang="ts">
/**
 * 分配角色对话框：以复选框列表展示 /system/role/all 的角色，
 * 已停用角色选项禁用（避免新授权），提交调用 /system/user/{id}/roles。
 */
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { RoleBrief } from '@/api/system/role'
import { getAllRolesApi } from '@/api/system/role'
import type { UserVO } from '@/api/system/user'
import { updateUserRolesApi } from '@/api/system/user'

defineOptions({ name: 'UserRoleDialog' })

const emit = defineEmits<{
  /** 分配成功（父级刷新列表） */
  success: []
}>()

/** 对话框显示状态 */
const visible = ref(false)

/** 当前用户 ID（GUID 字符串） */
const userId = ref('')

/** 角色选项加载中 */
const loading = ref(false)

/** 提交中 */
const submitting = ref(false)

/** 角色选项 */
const roles = ref<RoleBrief[]>([])

/** 已勾选的角色 ID 集合 */
const selectedRoleIds = ref<string[]>([])

/**
 * 打开对话框。
 *
 * @param row 目标用户行（初始勾选取自行数据的 roles）
 */
async function open(row: UserVO): Promise<void> {
  userId.value = row.id
  selectedRoleIds.value = row.roles.map((item) => item.id)
  visible.value = true
  loading.value = true
  try {
    roles.value = await getAllRolesApi()
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    loading.value = false
  }
}

/** 提交分配 */
async function handleSubmit(): Promise<void> {
  submitting.value = true
  try {
    await updateUserRolesApi(userId.value, selectedRoleIds.value)
    ElMessage.success('分配角色成功')
    visible.value = false
    emit('success')
  } finally {
    submitting.value = false
  }
}

defineExpose({ open })
</script>

<template>
  <el-dialog
    v-model="visible"
    title="分配角色"
    width="480px"
    :close-on-click-modal="false"
    append-to-body
  >
    <div v-loading="loading" class="role-check-list">
      <template v-if="roles.length > 0">
        <el-checkbox-group v-model="selectedRoleIds" class="role-check-group">
          <el-checkbox
            v-for="role in roles"
            :key="role.id"
            :value="role.id"
            :disabled="role.status === 0"
          >
            {{ role.roleName }}
            <span class="role-key">{{ role.roleKey }}</span>
            <el-tag v-if="role.status === 0" size="small" type="danger">停用</el-tag>
          </el-checkbox>
        </el-checkbox-group>
      </template>
      <el-empty v-else-if="!loading" :image-size="80" description="暂无可分配角色" />
    </div>
    <template #footer>
      <el-button @click="visible = false">取 消</el-button>
      <el-button
        type="primary"
        :loading="submitting"
        :disabled="roles.length === 0"
        @click="handleSubmit"
      >
        确 定
      </el-button>
    </template>
  </el-dialog>
</template>

<style lang="scss" scoped>
.role-check-list {
  min-height: 160px;
}

.role-check-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: flex-start;
}

.role-key {
  margin-left: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
