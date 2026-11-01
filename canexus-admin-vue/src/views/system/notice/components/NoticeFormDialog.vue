<script setup lang="ts">
/**
 * 新增 / 编辑通知公告对话框。
 *
 * 类型经 useNoticeTypeDict() 动态获取（sys_notice_type，本地常量兜底）；
 * 编辑时经详情接口拉取全量回显（列表数据内容可能被后端截断）。
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { NoticeVO } from '@/api/system/notice'
import { createNoticeApi, getNoticeApi, updateNoticeApi } from '@/api/system/notice'
import { useNoticeTypeDict } from '@/utils/dict'

defineOptions({ name: 'NoticeFormDialog' })

/** 公告类型字典（useDict('sys_notice_type')，未拉取到时回退本地常量） */
const noticeTypeItems = useNoticeTypeDict()

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

/** 当前编辑的公告 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 回显加载中（编辑时拉取详情） */
const detailLoading = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  title: '',
  noticeType: 1,
  content: '',
  status: 1,
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 表单校验规则 */
const rules: FormRules = {
  title: [
    { required: true, message: '请输入公告标题', trigger: 'blur' },
    { max: 100, message: '公告标题不能超过 100 个字符', trigger: 'blur' },
  ],
  noticeType: [{ required: true, message: '请选择公告类型', trigger: 'change' }],
  content: [
    { required: true, message: '请输入公告内容', trigger: 'blur' },
    { max: 5000, message: '公告内容不能超过 5000 个字符', trigger: 'blur' },
  ],
  status: [{ required: true, message: '请选择状态', trigger: 'change' }],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（提供 id，全量数据经详情接口回显）
 */
async function open(mode: 'create' | 'edit', row?: NoticeVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm())
  visible.value = true
  await nextTick()
  formRef.value?.clearValidate()
  if (isEdit.value && row) {
    detailLoading.value = true
    try {
      const detail = await getNoticeApi(row.id)
      Object.assign(form, {
        title: detail.title,
        noticeType: detail.noticeType,
        content: detail.content,
        status: detail.status,
        description: detail.description ?? '',
      })
    } catch {
      // 详情拉取失败回退行数据回显（错误提示在请求层）
      Object.assign(form, {
        title: row.title,
        noticeType: row.noticeType,
        content: row.content,
        status: row.status,
        description: row.description ?? '',
      })
    } finally {
      detailLoading.value = false
    }
  }
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
      title: form.title.trim(),
      noticeType: form.noticeType,
      content: form.content,
      status: form.status,
      description: form.description.trim(),
    }
    if (isEdit.value) {
      await updateNoticeApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createNoticeApi(data)
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
    :title="isEdit ? '编辑公告' : '新增公告'"
    width="640px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form
      ref="formRef"
      v-loading="detailLoading"
      :model="form"
      :rules="rules"
      label-width="80px"
    >
      <el-form-item label="公告标题" prop="title">
        <el-input v-model="form.title" placeholder="请输入公告标题" maxlength="100" />
      </el-form-item>
      <el-form-item label="公告类型" prop="noticeType">
        <el-radio-group v-model="form.noticeType">
          <el-radio v-for="item in noticeTypeItems" :key="item.value" :value="Number(item.value)">
            {{ item.label }}
          </el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="公告内容" prop="content">
        <el-input
          v-model="form.content"
          type="textarea"
          :rows="6"
          maxlength="5000"
          show-word-limit
          placeholder="请输入公告内容"
        />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">正常</el-radio>
          <el-radio :value="0">关闭</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="备注" prop="description">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="2"
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
