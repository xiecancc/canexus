<script setup lang="ts">
/**
 * 新增 / 编辑租户对话框。
 *
 * 租户标识唯一性由后端校验；编辑模式标识禁用修改。
 * 编辑回显直接使用列表行数据（契约无独立详情接口）。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { TenantVO } from '@/api/system/tenant'
import { createTenantApi, updateTenantApi } from '@/api/system/tenant'

defineOptions({ name: 'TenantFormDialog' })

const emit = defineEmits<{
  /** 提交成功（父级刷新列表） */
  success: []
}>()

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 是否编辑模式 */
const isEdit = ref(false)

/** 当前编辑的租户 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  tenantKey: '',
  tenantName: '',
  contactPhone: '',
  status: 1,
  description: '',
  expireTime: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 表单校验规则 */
const rules: FormRules = {
  tenantKey: [
    { required: true, message: '请输入租户标识', trigger: 'blur' },
    { max: 50, message: '租户标识不能超过 50 个字符', trigger: 'blur' },
  ],
  tenantName: [
    { required: true, message: '请输入租户名称', trigger: 'blur' },
    { max: 100, message: '租户名称不能超过 100 个字符', trigger: 'blur' },
  ],
  contactPhone: [{ pattern: /^1[3-9]\d{9}$/, message: '请输入正确的手机号码', trigger: 'blur' }],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: TenantVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm(), {
    tenantKey: row?.tenantKey ?? '',
    tenantName: row?.tenantName ?? '',
    contactPhone: row?.contactPhone ?? '',
    status: row?.status ?? 1,
    description: row?.description ?? '',
    expireTime: row?.expireTime ?? '',
  })
  visible.value = true
  await nextTick()
  formRef.value?.clearValidate()
}

/** 提交表单 */
async function handleSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  submitting.value = true
  try {
    const data = {
      tenantKey: form.tenantKey.trim(),
      tenantName: form.tenantName.trim(),
      contactPhone: form.contactPhone.trim(),
      status: form.status,
      description: form.description.trim(),
      expireTime: form.expireTime || undefined,
    }
    if (isEdit.value) {
      await updateTenantApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createTenantApi(data)
      ElMessage.success('新增成功')
    }
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
    :title="isEdit ? '编辑租户' : '新增租户'"
    width="560px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="租户标识" prop="tenantKey">
        <el-input
          v-model="form.tenantKey"
          placeholder="请输入租户标识（唯一）"
          maxlength="50"
          :disabled="isEdit"
        />
      </el-form-item>
      <el-form-item label="租户名称" prop="tenantName">
        <el-input v-model="form.tenantName" placeholder="请输入租户名称" maxlength="100" />
      </el-form-item>
      <el-form-item label="联系电话" prop="contactPhone">
        <el-input v-model="form.contactPhone" placeholder="请输入联系电话" maxlength="11" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">启用</el-radio>
          <el-radio :value="0">停用</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="过期时间" prop="expireTime">
        <el-date-picker
          v-model="form.expireTime"
          type="datetime"
          placeholder="留空表示长期有效"
          value-format="YYYY-MM-DD HH:mm:ss"
          style="width: 100%"
        />
      </el-form-item>
      <el-form-item label="备注" prop="description">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="3"
          maxlength="200"
          show-word-limit
          placeholder="请输入备注"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取 消</el-button>
      <el-button type="primary" :loading="submitting" @click="handleSubmit">确 定</el-button>
    </template>
  </el-dialog>
</template>
