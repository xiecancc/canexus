<script setup lang="ts">
/**
 * 新增 / 编辑参数配置对话框。
 *
 * 编辑回显直接使用列表行数据（契约无单条详情接口）；
 * 内置参数（builtIn）仅不可删除，键名与键值允许维护。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { ConfigVO } from '@/api/system/config'
import { createConfigApi, updateConfigApi } from '@/api/system/config'

defineOptions({ name: 'ConfigFormDialog' })

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

/** 当前编辑的参数 ID */
const editId = ref('')

/** 当前编辑参数的乐观锁版本号（取自行数据，写请求回传至 URL） */
const editVersion = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  configName: '',
  configKey: '',
  configValue: '',
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 表单校验规则 */
const rules: FormRules = {
  configName: [
    { required: true, message: '请输入参数名称', trigger: 'blur' },
    { max: 100, message: '参数名称不能超过 100 个字符', trigger: 'blur' },
  ],
  configKey: [
    { required: true, message: '请输入参数键名', trigger: 'blur' },
    { max: 100, message: '参数键名不能超过 100 个字符', trigger: 'blur' },
  ],
  configValue: [
    { required: true, message: '请输入参数键值', trigger: 'blur' },
    { max: 200, message: '参数键值不能超过 200 个字符', trigger: 'blur' },
  ],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: ConfigVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  editVersion.value = row?.version ?? ''
  Object.assign(form, getDefaultForm(), {
    configName: row?.configName ?? '',
    configKey: row?.configKey ?? '',
    configValue: row?.configValue ?? '',
    description: row?.description ?? '',
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
      configName: form.configName.trim(),
      configKey: form.configKey.trim(),
      configValue: form.configValue,
      description: form.description.trim(),
    }
    if (isEdit.value) {
      await updateConfigApi(editId.value, editVersion.value, data)
      ElMessage.success('修改成功')
    } else {
      await createConfigApi(data)
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
    :title="isEdit ? '编辑参数' : '新增参数'"
    width="560px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="参数名称" prop="configName">
        <el-input v-model="form.configName" placeholder="请输入参数名称" maxlength="100" />
      </el-form-item>
      <el-form-item label="参数键名" prop="configKey">
        <el-input
          v-model="form.configKey"
          placeholder="请输入参数键名（如 sys.index.skinName）"
          maxlength="100"
        />
      </el-form-item>
      <el-form-item label="参数键值" prop="configValue">
        <el-input v-model="form.configValue" placeholder="请输入参数键值" maxlength="200" />
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
