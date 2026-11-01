<script setup lang="ts">
/**
 * 新增 / 编辑角色对话框。
 *
 * - 菜单授权树来自 /system/menu/tree，父子联动（check-strictly=false）
 * - 编辑回显：后端 menuIds 为全选节点集合（含父与子），仅对叶子节点
 *   setChecked(id, true, false)，父级由树联动自动计算全选 / 半选状态
 * - 提交时收集全选 + 半选节点，符合后端"含父与子"的存储约定
 */
import { nextTick, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules, TreeInstance } from 'element-plus'
import type { RoleVO } from '@/api/system/role'
import { createRoleApi, getRoleApi, updateRoleApi } from '@/api/system/role'
import type { MenuVO } from '@/api/system/menu'
import { getCheckedMenuIds } from '@/api/system/menu'
import { getMenuTreeApi } from '@/api/system/menu'
import { SYS_DATA_SCOPE } from '@/utils/dict'

defineOptions({ name: 'RoleFormDialog' })

const emit = defineEmits<{
  /** 提交成功（父级刷新列表） */
  success: []
}>()

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 菜单授权树引用 */
const menuTreeRef = ref<TreeInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 是否编辑模式 */
const isEdit = ref(false)

/** 当前编辑的角色 ID（GUID 字符串） */
const editId = ref('')

/** 提交中 */
const submitting = ref(false)

/** 菜单树加载中 */
const menuLoading = ref(false)

/** 菜单树数据 */
const menuTree = ref<MenuVO[]>([])

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  roleName: '',
  roleKey: '',
  dataScope: 1,
  status: 1,
  description: '',
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 权限字符：字母开头，可含数字、下划线、点、冒号、中划线 */
const ROLE_KEY_PATTERN = /^[a-zA-Z][a-zA-Z0-9_:.-]*$/

/** 表单校验规则 */
const rules: FormRules = {
  roleName: [
    { required: true, message: '请输入角色名称', trigger: 'blur' },
    { max: 30, message: '角色名称不能超过 30 个字符', trigger: 'blur' },
  ],
  roleKey: [
    { required: true, message: '请输入权限字符', trigger: 'blur' },
    {
      pattern: ROLE_KEY_PATTERN,
      message: '权限字符需以字母开头，可含数字、下划线、点、冒号、中划线',
      trigger: 'blur',
    },
  ],
}

/** 加载菜单授权树（失败时置空，由模板展示空态） */
async function loadMenuTree(): Promise<void> {
  menuLoading.value = true
  try {
    menuTree.value = await getMenuTreeApi()
  } catch {
    // 错误提示由请求层统一处理
    menuTree.value = []
  } finally {
    menuLoading.value = false
  }
}

/**
 * 打开对话框。
 *
 * @param mode create 新增 / edit 编辑
 * @param row  编辑时的行数据（用于获取角色 ID）
 */
async function open(mode: 'create' | 'edit', row?: RoleVO): Promise<void> {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  Object.assign(form, getDefaultForm())
  menuTree.value = []
  visible.value = true
  await nextTick()
  formRef.value?.clearValidate()

  // 并行加载菜单树与角色详情，避免回显早于树渲染
  const loadDetail = isEdit.value
    ? getRoleApi(editId.value).catch(() => undefined)
    : Promise.resolve(undefined)
  await Promise.all([loadMenuTree(), loadDetail])
  const detail = await loadDetail
  await nextTick()
  if (detail) {
    Object.assign(form, {
      roleName: detail.roleName,
      roleKey: detail.roleKey,
      dataScope: detail.dataScope,
      status: detail.status,
      description: detail.description,
    })
    // 仅显式勾选叶子节点（不向下联动），父级由树联动自动计算
    getCheckedMenuIds(menuTree.value, detail.menuIds).forEach((id) => {
      menuTreeRef.value?.setChecked(id, true, false)
    })
  }
}

/** 提交表单 */
async function handleSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  if (menuTree.value.length === 0) {
    ElMessage.warning('菜单授权数据加载失败，请刷新后重试')
    return
  }
  // 全选节点 + 半选父级，构成"含父与子"的授权集合（节点 key 已是 GUID 字符串）
  const checkedKeys = menuTreeRef.value?.getCheckedKeys() ?? []
  const halfCheckedKeys = menuTreeRef.value?.getHalfCheckedKeys() ?? []
  const menuIds = [...checkedKeys, ...halfCheckedKeys].map(String)

  submitting.value = true
  try {
    const data = {
      roleName: form.roleName.trim(),
      roleKey: form.roleKey.trim(),
      dataScope: form.dataScope,
      status: form.status,
      description: form.description.trim(),
      menuIds,
    }
    if (isEdit.value) {
      await updateRoleApi(editId.value, data)
      ElMessage.success('修改成功')
    } else {
      await createRoleApi(data)
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
    :title="isEdit ? '编辑角色' : '新增角色'"
    width="640px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="角色名称" prop="roleName">
        <el-input v-model="form.roleName" placeholder="请输入角色名称" maxlength="30" />
      </el-form-item>
      <el-form-item label="权限字符" prop="roleKey">
        <el-input v-model="form.roleKey" placeholder="如 system:admin" maxlength="100" />
      </el-form-item>
      <el-form-item label="状态" prop="status">
        <el-radio-group v-model="form.status">
          <el-radio :value="1">正常</el-radio>
          <el-radio :value="0">停用</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="数据范围" prop="dataScope">
        <el-radio-group v-model="form.dataScope">
          <el-radio v-for="item in SYS_DATA_SCOPE" :key="item.value" :value="Number(item.value)">
            {{ item.label }}
          </el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="菜单授权">
        <div v-loading="menuLoading" class="menu-tree-wrapper">
          <el-tree
            ref="menuTreeRef"
            :data="menuTree"
            :props="{ label: 'menuName', children: 'children' }"
            node-key="id"
            show-checkbox
            default-expand-all
            :check-strictly="false"
            empty-text="暂无菜单数据"
            class="menu-tree"
          />
        </div>
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
.menu-tree-wrapper {
  width: 100%;
  min-height: 120px;
}

.menu-tree {
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  padding: 6px;
}
</style>
