<script setup lang="ts">
/**
 * 新增 / 编辑部门对话框。
 *
 * - 上级部门下拉为"顶级部门 + 父级传入的部门树"，新增子级时自动带入父级
 * - 编辑时行数据已包含全部字段，直接回显不重复请求详情
 * - 上级部门禁止选择自身，避免形成环形结构（层级环路另由后端校验兜底）
 */
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { DeptVO } from '@/api/system/dept'
import { createDeptApi, updateDeptApi } from '@/api/system/dept'

defineOptions({ name: 'DeptFormDialog' })

/** 空 GUID（后端树根父 ID 约定，顶级部门以此为父） */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const props = defineProps<{
  /** 部门树数据（父级页面已加载） */
  deptTree: DeptVO[]
}>()

const emit = defineEmits<{
  /** 提交成功（父级刷新树表格） */
  success: []
}>()

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 是否编辑模式 */
const isEdit = ref(false)

/** 当前编辑的部门 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  parentId: EMPTY_GUID,
  deptName: '',
  orderNum: 0,
  leader: '',
  phone: '',
  email: '',
  status: 1,
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 手机号格式 */
const PHONE_PATTERN = /^1[3-9]\d{9}$/

/** 邮箱格式 */
const EMAIL_PATTERN = /^[\w.+-]+@[\w-]+(\.[\w-]+)+$/

/** 表单校验规则 */
const rules: FormRules = {
  parentId: [
    { required: true, message: '请选择上级部门', trigger: 'change' },
    {
      validator: (_rule, value: string, callback) => {
        if (isEdit.value && value === editId.value) {
          callback(new Error('上级部门不能选择自身'))
        } else {
          callback()
        }
      },
      trigger: 'change',
    },
  ],
  deptName: [
    { required: true, message: '请输入部门名称', trigger: 'blur' },
    { min: 2, max: 50, message: '部门名称长度为 2 到 50 个字符', trigger: 'blur' },
  ],
  leader: [{ max: 20, message: '负责人不能超过 20 个字符', trigger: 'blur' }],
  phone: [{ pattern: PHONE_PATTERN, message: '手机号格式不正确', trigger: 'blur' }],
  email: [{ pattern: EMAIL_PATTERN, message: '邮箱格式不正确', trigger: 'blur' }],
}

/** 上级部门选项：顶级部门节点包裹完整部门树 */
const parentOptions = computed<DeptVO[]>(() => [
  {
    id: EMPTY_GUID,
    parentId: '',
    deptName: '顶级部门',
    orderNum: 0,
    leader: '',
    phone: '',
    email: '',
    status: 1,
    createdAt: '',
    children: props.deptTree,
  },
])

/**
 * 打开对话框。
 *
 * @param mode     create 新增 / edit 编辑
 * @param row      编辑时的行数据（字段完整，直接回显）
 * @param parentId 新增子级时的默认上级部门 ID
 */
function open(mode: 'create' | 'edit', row?: DeptVO, parentId?: string): void {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm(), {
    parentId: mode === 'edit' ? (row?.parentId ?? EMPTY_GUID) : (parentId ?? EMPTY_GUID),
  })
  if (mode === 'edit' && row) {
    Object.assign(form, {
      deptName: row.deptName,
      orderNum: row.orderNum,
      leader: row.leader,
      phone: row.phone,
      email: row.email,
      status: row.status,
    })
  }
  visible.value = true
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
      parentId: form.parentId,
      deptName: form.deptName.trim(),
      orderNum: form.orderNum,
      leader: form.leader.trim(),
      phone: form.phone.trim(),
      email: form.email.trim(),
      status: form.status,
    }
    if (isEdit.value) {
      await updateDeptApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createDeptApi(data)
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
    :title="isEdit ? '编辑部门' : '新增部门'"
    width="600px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="上级部门" prop="parentId">
        <el-tree-select
          v-model="form.parentId"
          :data="parentOptions"
          :props="{ label: 'deptName', children: 'children' }"
          node-key="id"
          check-strictly
          default-expand-all
          placeholder="请选择上级部门"
          style="width: 100%"
        />
      </el-form-item>
      <el-form-item label="部门名称" prop="deptName">
        <el-input v-model="form.deptName" placeholder="请输入部门名称" maxlength="50" />
      </el-form-item>
      <el-form-item label="显示排序" prop="orderNum">
        <el-input-number v-model="form.orderNum" :min="0" :max="9999" controls-position="right" />
      </el-form-item>
      <el-form-item label="负责人" prop="leader">
        <el-input v-model="form.leader" placeholder="请输入负责人" maxlength="20" />
      </el-form-item>
      <el-form-item label="联系电话" prop="phone">
        <el-input v-model="form.phone" placeholder="请输入联系电话" maxlength="11" />
      </el-form-item>
      <el-form-item label="邮箱" prop="email">
        <el-input v-model="form.email" placeholder="请输入邮箱" maxlength="50" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">正常</el-radio>
          <el-radio :value="0">停用</el-radio>
        </el-radio-group>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取 消</el-button>
      <el-button type="primary" :loading="submitting" @click="handleSubmit">确 定</el-button>
    </template>
  </el-dialog>
</template>
