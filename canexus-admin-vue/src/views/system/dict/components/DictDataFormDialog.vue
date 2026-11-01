<script setup lang="ts">
/**
 * 新增 / 编辑字典数据对话框。
 *
 * 所属字典类型由列表页固定（props.dictType），表单内不再编辑；
 * tagType 为标签色（1-5），提交后业务侧按该色渲染字典标签。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { DictDataVO } from '@/api/system/dict'
import { createDictDataApi, updateDictDataApi } from '@/api/system/dict'
import { DICT_TAG_TYPE_MAP } from '@/composables/useDict'

defineOptions({ name: 'DictDataFormDialog' })

const props = defineProps<{
  /** 所属字典类型（新增时归属该类型） */
  dictType: string
}>()

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

/** 当前编辑的字典数据 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 标签色选项（1-5 与后端契约一致） */
const tagTypeOptions = Object.entries(DICT_TAG_TYPE_MAP).map(([value, tagType]) => ({
  value: Number(value),
  label: tagType,
}))

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  dictLabel: '',
  dictValue: '',
  tagType: 1,
  orderNum: 1,
  status: 1,
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 表单校验规则 */
const rules: FormRules = {
  dictLabel: [
    { required: true, message: '请输入字典标签', trigger: 'blur' },
    { max: 100, message: '字典标签不能超过 100 个字符', trigger: 'blur' },
  ],
  dictValue: [
    { required: true, message: '请输入字典键值', trigger: 'blur' },
    { max: 100, message: '字典键值不能超过 100 个字符', trigger: 'blur' },
  ],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: DictDataVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm(), {
    dictLabel: row?.dictLabel ?? '',
    dictValue: row?.dictValue ?? '',
    tagType: row?.tagType ?? 1,
    orderNum: row?.orderNum ?? 1,
    status: row?.status ?? 1,
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
      dictType: props.dictType,
      dictLabel: form.dictLabel.trim(),
      dictValue: form.dictValue.trim(),
      tagType: form.tagType,
      description: form.description.trim(),
      orderNum: form.orderNum,
      status: form.status,
    }
    if (isEdit.value) {
      await updateDictDataApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createDictDataApi(data)
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
    :title="isEdit ? '编辑字典数据' : '新增字典数据'"
    width="560px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="字典标签" prop="dictLabel">
        <el-input v-model="form.dictLabel" placeholder="请输入字典标签" maxlength="100" />
      </el-form-item>
      <el-form-item label="字典键值" prop="dictValue">
        <el-input v-model="form.dictValue" placeholder="请输入字典键值" maxlength="100" />
      </el-form-item>
      <el-form-item label="标签色" prop="tagType">
        <el-select v-model="form.tagType" style="width: 100%">
          <el-option
            v-for="option in tagTypeOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          >
            <el-tag :type="option.label" size="small">{{ option.label }}</el-tag>
          </el-option>
        </el-select>
      </el-form-item>
      <el-form-item label="显示排序" prop="orderNum">
        <el-input-number v-model="form.orderNum" :min="0" :max="9999" controls-position="right" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">启用</el-radio>
          <el-radio :value="0">停用</el-radio>
        </el-radio-group>
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
