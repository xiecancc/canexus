<script setup lang="ts">
/**
 * 重置密码对话框：6-20 位且需同时包含字母和数字，提交调用 /system/user/{id}/password。
 */
import { reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { UserVO } from '@/api/system/user'
import { updateUserPasswordApi } from '@/api/system/user'

defineOptions({ name: 'UserPwdDialog' })

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 当前用户 ID（GUID 字符串） */
const userId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单数据 */
const form = reactive({ password: '' })

/** 密码强度：6-20 位且同时包含字母与数字，不允许空白字符 */
const PWD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d)[^\s]{6,20}$/

/** 表单校验规则 */
const rules: FormRules = {
  password: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    {
      pattern: PWD_PATTERN,
      message: '密码为 6-20 位，且需同时包含字母和数字',
      trigger: 'blur',
    },
  ],
}

/**
 * 打开对话框。
 *
 * @param row 目标用户行
 */
function open(row: UserVO): void {
  userId.value = row.id
  form.password = ''
  visible.value = true
}

/** 提交重置 */
async function handleSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  submitting.value = true
  try {
    await updateUserPasswordApi(userId.value, form.password)
    ElMessage.success('密码重置成功')
    visible.value = false
  } finally {
    submitting.value = false
  }
}

defineExpose({ open })
</script>

<template>
  <el-dialog
    v-model="visible"
    title="重置密码"
    width="440px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
      <el-form-item label="新密码" prop="password">
        <el-input
          v-model="form.password"
          type="password"
          show-password
          placeholder="6-20 位，需同时包含字母和数字"
          maxlength="20"
          autocomplete="new-password"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取 消</el-button>
      <el-button type="primary" :loading="submitting" @click="handleSubmit">确 定</el-button>
    </template>
  </el-dialog>
</template>
