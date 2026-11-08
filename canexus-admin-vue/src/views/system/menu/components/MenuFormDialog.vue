<script setup lang="ts">
/**
 * 新增 / 编辑菜单对话框。
 *
 * - 按菜单类型（1 目录 / 2 菜单 / 3 按钮）动态显隐字段：目录无组件路径与缓存，
 *   按钮仅需权限标识（必填）、排序与状态
 * - 类型切换时清除校验痕迹；编辑时行数据字段完整，直接回显
 * - 图标通过 IconPicker 选择；上级菜单禁止选择自身，避免环形层级
 */
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import type { MenuSaveRequest, MenuType, MenuVO } from '@/api/system/menu'
import { createMenuApi, getMenuTreeApi, updateMenuApi } from '@/api/system/menu'
import IconPicker from '@/components/IconPicker.vue'

defineOptions({ name: 'MenuFormDialog' })

/** 空 GUID（后端树根父 ID 约定，顶级菜单以此为父） */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const emit = defineEmits<{
  /** 提交成功（父级刷新树表格） */
  success: []
}>()

/** 菜单类型选项 */
const MENU_TYPE_OPTIONS: { value: MenuType; label: string }[] = [
  { value: 1, label: '目录' },
  { value: 2, label: '菜单' },
  { value: 3, label: '按钮' },
]

/** 表单引用 */
const formRef = ref<FormInstance>()

/** 对话框显示状态 */
const visible = ref(false)

/** 是否编辑模式 */
const isEdit = ref(false)

/** 当前编辑的菜单 ID */
const editId = ref('')

/** 当前编辑菜单的乐观锁版本号（取自行数据，写请求回传至 URL） */
const editVersion = ref('')

/** 提交中 */
const submitting = ref(false)

/** 菜单树加载中 */
const menuLoading = ref(false)

/** 菜单树数据（打开时加载完整树，不受列表页搜索条件影响） */
const menuTree = ref<MenuVO[]>([])

/** 表单默认值工厂 */
const getDefaultForm = () => ({
  parentId: EMPTY_GUID,
  menuType: 1 as MenuType,
  menuName: '',
  path: '',
  component: '',
  perms: '',
  icon: '',
  orderNum: 0,
  visible: 1,
  keepAlive: false,
  status: 1,
})

/** 表单数据 */
const form = reactive(getDefaultForm())

/** 动态校验规则：目录/菜单必填路由地址，按钮必填权限标识 */
const rules = computed<FormRules>(() => ({
  parentId: [
    { required: true, message: '请选择上级菜单', trigger: 'change' },
    {
      validator: (_rule, value: string, callback) => {
        if (isEdit.value && value === editId.value) {
          callback(new Error('上级菜单不能选择自身'))
        } else {
          callback()
        }
      },
      trigger: 'change',
    },
  ],
  menuName: [
    { required: true, message: '请输入菜单名称', trigger: 'blur' },
    { max: 50, message: '菜单名称不能超过 50 个字符', trigger: 'blur' },
  ],
  ...(form.menuType !== 3
    ? { path: [{ required: true, message: '请输入路由地址', trigger: 'blur' }] }
    : {}),
  ...(form.menuType === 3
    ? { perms: [{ required: true, message: '请输入权限标识', trigger: 'blur' }] }
    : {}),
}))

/** 上级菜单选项：顶级菜单节点包裹完整菜单树 */
const parentOptions = computed<MenuVO[]>(() => [
  {
    id: EMPTY_GUID,
    version: '',
    parentId: '',
    menuType: 1,
    menuName: '顶级菜单',
    path: '',
    component: '',
    perms: '',
    icon: '',
    orderNum: 0,
    visible: 1,
    keepAlive: false,
    status: 1,
    createdAt: '',
    children: menuTree.value,
  },
])

/** 加载完整菜单树（失败时置空，由模板展示空态） */
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

// 类型切换后清除校验痕迹（必填字段随类型变化）
watch(
  () => form.menuType,
  async () => {
    await nextTick()
    formRef.value?.clearValidate()
  },
)

/**
 * 打开对话框。
 *
 * @param mode     create 新增 / edit 编辑
 * @param row      编辑时的行数据（字段完整，直接回显）
 * @param parentId 新增子级时的默认上级菜单 ID
 */
function open(mode: 'create' | 'edit', row?: MenuVO, parentId?: string): void {
  isEdit.value = mode === 'edit'
  editId.value = row?.id ?? ''
  editVersion.value = row?.version ?? ''
  Object.assign(form, getDefaultForm(), {
    parentId: mode === 'edit' ? (row?.parentId ?? EMPTY_GUID) : (parentId ?? EMPTY_GUID),
  })
  if (mode === 'edit' && row) {
    Object.assign(form, {
      menuType: row.menuType,
      menuName: row.menuName,
      path: row.path,
      component: row.component ?? '',
      perms: row.perms ?? '',
      icon: row.icon ?? '',
      orderNum: row.orderNum,
      visible: row.visible,
      keepAlive: row.keepAlive,
      status: row.status,
    })
  }
  visible.value = true
  void loadMenuTree()
  void nextTick().then(() => formRef.value?.clearValidate())
}

/** 提交表单（非适用字段按约定置空 / 省略） */
async function handleSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  submitting.value = true
  try {
    const data: MenuSaveRequest = {
      parentId: form.parentId,
      menuType: form.menuType,
      menuName: form.menuName.trim(),
      path: form.menuType === 3 ? '' : form.path.trim(),
      component: form.menuType === 2 ? form.component.trim() || undefined : undefined,
      perms: form.perms.trim() || undefined,
      icon: form.menuType === 3 ? undefined : form.icon || undefined,
      orderNum: form.orderNum,
      visible: form.menuType === 3 ? 1 : form.visible,
      keepAlive: form.menuType === 2 ? form.keepAlive : false,
      status: form.status,
    }
    if (isEdit.value) {
      await updateMenuApi(editId.value, editVersion.value, data)
      ElMessage.success('修改成功')
    } else {
      await createMenuApi(data)
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
    :title="isEdit ? '编辑菜单' : '新增菜单'"
    width="640px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
      <el-form-item label="菜单类型" prop="menuType">
        <el-radio-group v-model="form.menuType">
          <el-radio-button v-for="item in MENU_TYPE_OPTIONS" :key="item.value" :value="item.value">
            {{ item.label }}
          </el-radio-button>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="上级菜单" prop="parentId">
        <el-tree-select
          v-model="form.parentId"
          v-loading="menuLoading"
          :data="parentOptions"
          :props="{ label: 'menuName', children: 'children' }"
          node-key="id"
          check-strictly
          default-expand-all
          placeholder="请选择上级菜单"
          style="width: 100%"
        />
      </el-form-item>
      <el-form-item v-if="form.menuType !== 3" label="图标" prop="icon">
        <IconPicker v-model="form.icon" />
      </el-form-item>
      <el-form-item label="菜单名称" prop="menuName">
        <el-input v-model="form.menuName" placeholder="请输入菜单名称" maxlength="50" />
      </el-form-item>
      <el-form-item v-if="form.menuType !== 3" label="路由地址" prop="path">
        <el-input
          v-model="form.path"
          placeholder="如 system（目录）或 user（菜单）"
          maxlength="100"
        />
      </el-form-item>
      <el-form-item v-if="form.menuType === 2" label="组件路径" prop="component">
        <el-input
          v-model="form.component"
          placeholder="相对 src/views，如 system/user/index"
          maxlength="200"
        />
      </el-form-item>
      <el-form-item label="权限标识" prop="perms">
        <el-input
          v-model="form.perms"
          :placeholder="
            form.menuType === 3 ? '按钮必填，如 system:user:add' : '选填，如 system:user:list'
          "
          maxlength="100"
        />
      </el-form-item>
      <el-form-item label="显示排序" prop="orderNum">
        <el-input-number v-model="form.orderNum" :min="0" :max="9999" controls-position="right" />
      </el-form-item>
      <el-form-item v-if="form.menuType !== 3" label="是否显示" prop="visible">
        <el-switch v-model="form.visible" :active-value="1" :inactive-value="0" />
      </el-form-item>
      <el-form-item v-if="form.menuType === 2" label="是否缓存" prop="keepAlive">
        <el-switch v-model="form.keepAlive" />
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
