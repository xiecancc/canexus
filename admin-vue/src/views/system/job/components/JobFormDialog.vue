<script setup lang="ts">
/**
 * 新增 / 编辑定时任务对话框。
 *
 * - Cron 表达式：前端常用格式校验（5-6 段，最终以后端校验为准），
 *   输入框旁"校验"按钮即时校验并提示（契约无独立校验端点）
 * - 任务类型：预置框架内置示例 Job 全名（后端种子尚未提供内置清单，以下为占位示例），
 *   支持手动输入完整类名
 * - 编辑回显直接使用列表行数据（契约无独立详情接口）
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { MagicStick } from '@element-plus/icons-vue'
import type { JobVO } from '@/api/monitor/job'
import { createJobApi, updateJobApi } from '@/api/monitor/job'
import { isValidCronExpression } from '@/utils/cron'

defineOptions({ name: 'JobFormDialog' })

const emit = defineEmits<{
  /** 提交成功（父级刷新列表） */
  success: []
}>()

/** 任务类型预置选项（占位示例，支持手动输入框架内置 Job 全名） */
const JOB_TYPE_OPTIONS = ['System.Jobs.SystemHealthJob', 'System.Jobs.JobCleanJob']

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 是否编辑模式 */
const isEdit = ref(false)

/** 当前编辑的任务 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  jobName: '',
  jobGroup: '',
  cronExpression: '',
  jobType: '',
  status: 0,
  concurrent: 1,
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** Cron 表达式常用格式校验（5-6 段，后端最终校验） */
const validateCron = (_rule: unknown, value: string, callback: (error?: Error) => void) => {
  if (!value?.trim()) {
    callback(new Error('请输入 Cron 执行表达式'))
    return
  }
  if (!isValidCronExpression(value)) {
    callback(
      new Error('Cron 格式不正确：需为 5 段（分 时 日 月 周）或 6 段（秒 分 时 日 月 周）表达式'),
    )
    return
  }
  callback()
}

/** 表单校验规则 */
const rules: FormRules = {
  jobName: [
    { required: true, message: '请输入任务名称', trigger: 'blur' },
    { max: 100, message: '任务名称不能超过 100 个字符', trigger: 'blur' },
  ],
  jobGroup: [
    { required: true, message: '请输入任务分组', trigger: 'blur' },
    { max: 50, message: '任务分组不能超过 50 个字符', trigger: 'blur' },
  ],
  cronExpression: [{ required: true, validator: validateCron, trigger: 'blur' }],
  jobType: [{ required: true, message: '请选择或输入任务类型', trigger: 'change' }],
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于回显）
 */
async function open(mode: 'create' | 'edit', row?: JobVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm(), {
    jobName: row?.jobName ?? '',
    jobGroup: row?.jobGroup ?? '',
    cronExpression: row?.cronExpression ?? '',
    jobType: row?.jobType ?? '',
    status: row?.status ?? 0,
    concurrent: row?.concurrent ?? 1,
    description: row?.description ?? '',
  })
  visible.value = true
  await nextTick()
  formRef.value?.clearValidate()
}

/** 点击"校验"按钮：即时校验 Cron 表达式并提示结果 */
function handleValidateCron(): void {
  if (!form.cronExpression.trim()) {
    ElMessage.warning('请先输入 Cron 表达式')
    return
  }
  if (isValidCronExpression(form.cronExpression)) {
    ElMessage.success('Cron 表达式格式正确')
  } else {
    ElMessage.error('Cron 格式不正确：需为 5-6 段表达式（支持 * ? / , - 组合）')
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
      jobName: form.jobName.trim(),
      jobGroup: form.jobGroup.trim(),
      cronExpression: form.cronExpression.trim(),
      jobType: form.jobType.trim(),
      status: form.status,
      concurrent: form.concurrent,
      description: form.description.trim(),
    }
    if (isEdit.value) {
      await updateJobApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createJobApi(data)
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
    :title="isEdit ? '编辑任务' : '新增任务'"
    width="600px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
      <el-form-item label="任务名称" prop="jobName">
        <el-input v-model="form.jobName" placeholder="请输入任务名称" maxlength="100" />
      </el-form-item>
      <el-form-item label="任务分组" prop="jobGroup">
        <el-input
          v-model="form.jobGroup"
          placeholder="请输入任务分组（如 DEFAULT）"
          maxlength="50"
        />
      </el-form-item>
      <el-form-item label="Cron表达式" prop="cronExpression">
        <div class="cron-row">
          <el-input
            v-model="form.cronExpression"
            placeholder="如 0 0/5 * * * ? 或 0 0 12 * * ?"
            maxlength="100"
          />
          <el-button :icon="MagicStick" @click="handleValidateCron">校验</el-button>
        </div>
      </el-form-item>
      <el-form-item label="任务类型" prop="jobType">
        <el-select
          v-model="form.jobType"
          filterable
          allow-create
          default-first-option
          placeholder="选择内置示例或输入 Job 全名"
          style="width: 100%"
        >
          <el-option v-for="item in JOB_TYPE_OPTIONS" :key="item" :label="item" :value="item" />
        </el-select>
      </el-form-item>
      <el-form-item label="允许并发" prop="concurrent">
        <el-switch v-model="form.concurrent" :active-value="1" :inactive-value="0" />
        <span class="form-tip">{{ form.concurrent === 1 ? '允许' : '禁止' }}</span>
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">运行</el-radio>
          <el-radio :value="0">暂停</el-radio>
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

<style lang="scss" scoped>
.cron-row {
  display: flex;
  gap: 8px;
  width: 100%;
}

.form-tip {
  margin-left: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
