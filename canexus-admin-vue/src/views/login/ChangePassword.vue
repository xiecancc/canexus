<script setup lang="ts">
/**
 * 自助修改密码页。
 *
 * 后端契约：POST /auth/change-password，校验原密码后更新哈希并清除强制改密标记，
 * 同时吊销该用户全部刷新令牌，因此改密成功后必须清理本地状态并回到登录页重新登录。
 * 该端点是 MustChangePasswordMiddleware 的白名单，待改密用户（40302）可正常调用。
 */
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { changePasswordApi } from '@/api/auth'
import { LOGIN_ROUTE_PATH } from '@/router/constants'
import { useAuthStore } from '@/stores/auth'
import { getMustChangePassword } from '@/utils/token'

defineOptions({ name: 'ChangePassword' })

const router = useRouter()
const authStore = useAuthStore()

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 提交中 */
const submitting = ref(false)

/** 是否由后端强制改密（决定提示文案） */
const isForced = computed(() => getMustChangePassword())

const form = reactive({
  oldPassword: '',
  newPassword: '',
  confirmPassword: '',
})

/** 密码强度：与后端 ChangePasswordRequest 一致，6-20 位且同时包含字母与数字 */
const PWD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d)[^\s]{6,20}$/

/** 表单校验规则 */
const rules: FormRules = {
  oldPassword: [{ required: true, message: '请输入原密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { pattern: PWD_PATTERN, message: '密码为 6-20 位，且需同时包含字母和数字', trigger: 'blur' },
  ],
  confirmPassword: [{ required: true, message: '请再次输入新密码', trigger: 'blur' }],
}

/** 提交改密：成功后清理认证状态并回到登录页（后端已吊销全部刷新令牌） */
async function handleSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  if (form.newPassword !== form.confirmPassword) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }
  submitting.value = true
  try {
    await changePasswordApi({
      oldPassword: form.oldPassword,
      newPassword: form.newPassword,
    })
    authStore.resetAuth()
    ElMessage.success('密码修改成功，请使用新密码重新登录')
    await router.push(LOGIN_ROUTE_PATH)
  } finally {
    submitting.value = false
  }
}

/** 暂不改密：退出登录回到登录页（登出端点在强制改密白名单内） */
async function handleLogout(): Promise<void> {
  try {
    await authStore.logout()
  } finally {
    await router.push(LOGIN_ROUTE_PATH)
  }
}
</script>

<template>
  <div class="change-password-page">
    <el-card class="change-password-card" shadow="always">
      <template #header>
        <div class="card-header">
          <span class="card-title">{{ isForced ? '首次登录，请修改初始密码' : '修改密码' }}</span>
          <span class="card-subtitle">修改成功后需要使用新密码重新登录</span>
        </div>
      </template>

      <el-alert
        v-if="isForced"
        type="warning"
        :closable="false"
        show-icon
        title="当前账号仍在使用初始密码"
        description="为保障账号安全，修改密码前仅可使用个人信息、导航与退出登录功能。"
        class="force-tip"
      />

      <el-form
        ref="formRef"
        :model="form"
        :rules="rules"
        label-width="90px"
        @keyup.enter="handleSubmit"
      >
        <el-form-item label="原密码" prop="oldPassword">
          <el-input
            v-model="form.oldPassword"
            type="password"
            show-password
            placeholder="请输入当前密码（首次登录为初始密码）"
            autocomplete="current-password"
          />
        </el-form-item>
        <el-form-item label="新密码" prop="newPassword">
          <el-input
            v-model="form.newPassword"
            type="password"
            show-password
            placeholder="6-20 位，需同时包含字母和数字"
            maxlength="20"
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item label="确认密码" prop="confirmPassword">
          <el-input
            v-model="form.confirmPassword"
            type="password"
            show-password
            placeholder="请再次输入新密码"
            maxlength="20"
            autocomplete="new-password"
          />
        </el-form-item>
      </el-form>

      <div class="card-actions">
        <el-button @click="handleLogout">退出登录</el-button>
        <el-button type="primary" :loading="submitting" @click="handleSubmit">确认修改</el-button>
      </div>
    </el-card>
  </div>
</template>

<style lang="scss" scoped>
.change-password-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
  background: var(--el-bg-color-page);
}

.change-password-card {
  width: 100%;
  max-width: 480px;
}

.card-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.card-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.card-subtitle {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.force-tip {
  margin-bottom: 20px;
}

.card-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 8px;
}
</style>
