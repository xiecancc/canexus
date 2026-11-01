<script setup lang="ts">
/**
 * 新增 / 编辑用户对话框。
 *
 * - 新增：账号可填、初始密码可选填（不填默认 123456）
 * - 编辑：账号只读、不修改密码，仅更新昵称/部门/联系方式/状态/角色/备注
 * - 部门选择复用父级传入的部门树数据，角色选项来自 /system/role/all
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { DeptVO } from '@/api/system/dept'
import type { RoleBrief } from '@/api/system/role'
import { getAllRolesApi } from '@/api/system/role'
import type { UserVO } from '@/api/system/user'
import { createUserApi, getUserApi, updateUserApi } from '@/api/system/user'

defineOptions({ name: 'UserFormDialog' })

const props = defineProps<{
  /** 部门树数据（父级页面已加载，避免重复请求） */
  deptTree: DeptVO[]
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

/** 当前编辑的用户 ID */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 角色选项加载中 */
const roleLoading = ref(false)

/** 角色选项（每次打开时刷新） */
const roleOptions = ref<RoleBrief[]>([])

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  userName: '',
  nickName: '',
  password: '',
  deptId: undefined as string | undefined,
  email: '',
  phone: '',
  status: 1,
  description: '',
  roleIds: [] as string[],
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 手机号格式 */
const PHONE_PATTERN = /^1[3-9]\d{9}$/

/** 邮箱格式 */
const EMAIL_PATTERN = /^[\w.+-]+@[\w-]+(\.[\w-]+)+$/

/** 表单校验规则 */
const rules: FormRules = {
  userName: [
    { required: true, message: '请输入账号', trigger: 'blur' },
    { min: 2, max: 30, message: '账号长度为 2 到 30 个字符', trigger: 'blur' },
  ],
  nickName: [
    { required: true, message: '请输入昵称', trigger: 'blur' },
    { max: 30, message: '昵称不能超过 30 个字符', trigger: 'blur' },
  ],
  password: [{ min: 5, max: 20, message: '密码长度为 5 到 20 位', trigger: 'blur' }],
  email: [{ pattern: EMAIL_PATTERN, message: '邮箱格式不正确', trigger: 'blur' }],
  phone: [{ pattern: PHONE_PATTERN, message: '手机号格式不正确', trigger: 'blur' }],
}

/** 加载角色选项 */
async function loadRoles(): Promise<void> {
  roleLoading.value = true
  try {
    roleOptions.value = await getAllRolesApi()
  } catch {
    // 错误提示由请求层统一处理
  } finally {
    roleLoading.value = false
  }
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于获取用户 ID）
 */
async function open(mode: 'create' | 'edit', row?: UserVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm())
  visible.value = true
  await nextTick()
  formRef.value?.clearValidate()
  void loadRoles()
  if (isEdit.value) {
    try {
      const detail = await getUserApi(editId.value)
      Object.assign(form, {
        userName: detail.userName,
        nickName: detail.nickName,
        deptId: detail.deptId || undefined,
        email: detail.email,
        phone: detail.phone,
        status: detail.status,
        description: detail.description,
        roleIds: [...detail.roleIds],
      })
    } catch {
      // 详情加载失败：保持空表单，用户可直接关闭
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
    if (isEdit.value) {
      await updateUserApi(editId.value, {
        nickName: form.nickName.trim(),
        deptId: form.deptId ?? '',
        email: form.email.trim(),
        phone: form.phone.trim(),
        status: form.status,
        description: form.description.trim(),
        roleIds: form.roleIds,
      })
      ElMessage.success('修改成功')
    } else {
      await createUserApi({
        userName: form.userName.trim(),
        nickName: form.nickName.trim(),
        password: form.password || undefined,
        deptId: form.deptId,
        email: form.email.trim(),
        phone: form.phone.trim(),
        status: form.status,
        description: form.description.trim(),
        roleIds: form.roleIds,
      })
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
    :title="isEdit ? '编辑用户' : '新增用户'"
    width="600px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="账号" prop="userName">
        <el-input
          v-model="form.userName"
          placeholder="请输入账号"
          maxlength="30"
          :disabled="isEdit"
        />
      </el-form-item>
      <el-form-item label="昵称" prop="nickName">
        <el-input v-model="form.nickName" placeholder="请输入昵称" maxlength="30" />
      </el-form-item>
      <el-form-item v-if="!isEdit" label="初始密码" prop="password">
        <el-input
          v-model="form.password"
          type="password"
          show-password
          placeholder="不填则默认为 123456"
          maxlength="20"
          autocomplete="new-password"
        />
      </el-form-item>
      <el-form-item label="部门" prop="deptId">
        <el-tree-select
          v-model="form.deptId"
          :data="props.deptTree"
          :props="{ label: 'deptName', children: 'children' }"
          node-key="id"
          check-strictly
          clearable
          default-expand-all
          placeholder="请选择部门"
          style="width: 100%"
        />
      </el-form-item>
      <el-form-item label="手机号" prop="phone">
        <el-input v-model="form.phone" placeholder="请输入手机号" maxlength="11" />
      </el-form-item>
      <el-form-item label="邮箱" prop="email">
        <el-input v-model="form.email" placeholder="请输入邮箱" maxlength="50" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">启用</el-radio>
          <el-radio :value="0">停用</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="角色" prop="roleIds">
        <el-select
          v-model="form.roleIds"
          multiple
          clearable
          :loading="roleLoading"
          placeholder="请选择角色（可多选）"
          style="width: 100%"
        >
          <el-option
            v-for="role in roleOptions"
            :key="role.id"
            :label="`${role.roleName}（${role.roleKey}）`"
            :value="role.id"
            :disabled="role.status === 0"
          />
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
