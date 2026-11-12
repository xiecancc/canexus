<script setup lang="ts">
/**
 * 新增 / 编辑岗位对话框。
 *
 * 编辑回显直接使用列表行数据（PostVO 已含全部业务字段与乐观锁 version）。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { PostSaveRequest, PostVO } from '@/api/system/post'
import { createPostApi, updatePostApi } from '@/api/system/post'

defineOptions({ name: 'PostFormDialog' })

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

/** 当前编辑的岗位 ID */
const editId = ref('')

/** 当前编辑岗位的乐观锁版本号（取自行数据，写请求回传至 URL） */
const editVersion = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  postName: '',
  postCode: '',
  orderNum: 0,
  status: 1,
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 表单校验规则 */
const rules: FormRules = {
  postName: [
    { required: true, message: '请输入岗位名称', trigger: 'blur' },
    { max: 50, message: '岗位名称不能超过 50 个字符', trigger: 'blur' },
  ],
  postCode: [
    { required: true, message: '请输入岗位编码', trigger: 'blur' },
    { max: 64, message: '岗位编码不能超过 64 个字符', trigger: 'blur' },
  ],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: PostVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  editVersion.value = row?.version ?? ''
  Object.assign(form, getDefaultForm(), {
    postName: row?.postName ?? '',
    postCode: row?.postCode ?? '',
    orderNum: row?.orderNum ?? 0,
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
    const data: PostSaveRequest = {
      postName: form.postName.trim(),
      postCode: form.postCode.trim(),
      orderNum: Number(form.orderNum) || 0,
      status: form.status,
      description: form.description.trim() || undefined,
    }
    if (isEdit.value) {
      await updatePostApi(editId.value, editVersion.value, data)
      ElMessage.success('修改成功')
    } else {
      await createPostApi(data)
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
    :title="isEdit ? '编辑岗位' : '新增岗位'"
    width="560px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="岗位名称" prop="postName">
        <el-input v-model="form.postName" placeholder="请输入岗位名称" maxlength="50" />
      </el-form-item>
      <el-form-item label="岗位编码" prop="postCode">
        <el-input v-model="form.postCode" placeholder="请输入岗位编码" maxlength="64" />
      </el-form-item>
      <el-form-item label="显示顺序" prop="orderNum">
        <el-input-number v-model="form.orderNum" :min="0" :max="9999" controls-position="right" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-select v-model="form.status" style="width: 100%">
          <el-option label="启用" :value="1" />
          <el-option label="停用" :value="0" />
        </el-select>
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
