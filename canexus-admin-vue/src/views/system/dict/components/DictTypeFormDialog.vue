<script setup lang="ts">
/**
 * 新增 / 编辑字典类型对话框。
 *
 * 字典类型（dictType）是字典数据的归属键且被业务引用，创建后不可修改，
 * 编辑模式下禁用输入；编辑回显直接使用列表行数据（契约无单条详情接口）。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { DictTypeVO } from '@/api/system/dict'
import { createDictTypeApi, updateDictTypeApi } from '@/api/system/dict'

defineOptions({ name: 'DictTypeFormDialog' })

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

/** 当前编辑的字典类型 ID */
const editId = ref('')

/** 当前编辑字典类型的乐观锁版本号（取自行数据，写请求回传至 URL） */
const editVersion = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  dictName: '',
  dictType: '',
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 字典类型键：字母开头，可含数字、下划线、点、冒号、中划线 */
const DICT_TYPE_PATTERN = /^[a-zA-Z][a-zA-Z0-9_:.-]*$/

/** 表单校验规则 */
const rules: FormRules = {
  dictName: [
    { required: true, message: '请输入字典名称', trigger: 'blur' },
    { max: 100, message: '字典名称不能超过 100 个字符', trigger: 'blur' },
  ],
  dictType: [
    { required: true, message: '请输入字典类型', trigger: 'blur' },
    {
      pattern: DICT_TYPE_PATTERN,
      message: '字典类型需以字母开头，可含数字、下划线、点、冒号、中划线',
      trigger: 'blur',
    },
  ],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: DictTypeVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  editVersion.value = row?.version ?? ''
  Object.assign(form, getDefaultForm(), {
    dictName: row?.dictName ?? '',
    dictType: row?.dictType ?? '',
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
      dictName: form.dictName.trim(),
      dictType: form.dictType.trim(),
      description: form.description.trim(),
    }
    if (isEdit.value) {
      await updateDictTypeApi(editId.value, editVersion.value, data)
      ElMessage.success('修改成功')
    } else {
      await createDictTypeApi(data)
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
    :title="isEdit ? '编辑字典类型' : '新增字典类型'"
    width="560px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="字典名称" prop="dictName">
        <el-input v-model="form.dictName" placeholder="请输入字典名称" maxlength="100" />
      </el-form-item>
      <el-form-item label="字典类型" prop="dictType">
        <el-input
          v-model="form.dictType"
          placeholder="请输入字典类型（创建后不可修改）"
          maxlength="100"
          :disabled="isEdit"
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
