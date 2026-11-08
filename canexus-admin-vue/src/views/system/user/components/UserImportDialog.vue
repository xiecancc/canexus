<script setup lang="ts">
/**
 * 用户导入对话框：步骤条引导（下载模板 → 上传文件 → 结果展示）。
 *
 * - 模板下载经 download.ts 文件流接口（GET /system/user/import-template）
 * - el-upload 手动上传，仅接受单个 .xlsx 文件（重复选择时替换原文件）
 * - 导入接口 POST /system/user/import（FormData: file），返回成功条数与失败明细
 */
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { UploadFile, UploadInstance, UploadRawFile } from 'element-plus'
import { Download, Upload } from '@element-plus/icons-vue'
import type { UserImportResult } from '@/api/system/user'
import { importUserApi } from '@/api/system/user'
import { downloadFile } from '@/utils/download'

defineOptions({ name: 'UserImportDialog' })

const emit = defineEmits<{
  /** 导入完成（含失败），父级刷新列表 */
  success: []
}>()

/** 对话框显示状态 */
const visible = ref(false)

/** 当前步骤：0 下载模板 / 1 上传文件 / 2 结果展示 */
const activeStep = ref(0)

/** 模板下载中 */
const templateDownloading = ref(false)

/** 导入提交中 */
const importing = ref(false)

/** 已选择的待上传文件 */
const selectedFile = ref<File | null>(null)

/** 导入结果 */
const importResult = ref<UserImportResult | null>(null)

/** el-upload 组件引用（limit=1 时替换文件用） */
const uploadRef = ref<UploadInstance>()

/** 打开对话框并重置步骤 */
function open(): void {
  activeStep.value = 0
  selectedFile.value = null
  importResult.value = null
  visible.value = true
}

/** 下载导入模板（文件名取不到时使用默认） */
async function handleDownloadTemplate(): Promise<void> {
  templateDownloading.value = true
  try {
    await downloadFile('/system/user/import-template', undefined, '用户导入模板.xlsx')
    ElMessage.success('模板下载成功')
  } catch {
    // 错误提示由下载层统一处理
  } finally {
    templateDownloading.value = false
  }
}

/** 文件选择变化：记录待上传文件（手动上传，不自动提交） */
function handleFileChange(file: UploadFile): void {
  selectedFile.value = (file.raw as File | undefined) ?? null
}

/** 文件移除：清空待上传文件 */
function handleFileRemove(): void {
  selectedFile.value = null
}

/** 超出单文件限制：替换为最新选择的文件 */
function handleExceed(files: File[]): void {
  uploadRef.value?.clearFiles()
  const file = files[0] as UploadRawFile | undefined
  if (file) {
    uploadRef.value?.handleStart(file)
  }
}

/** 开始导入：FormData 手动提交 */
async function handleImport(): Promise<void> {
  if (!selectedFile.value) {
    ElMessage.warning('请先选择要导入的 xlsx 文件')
    return
  }
  importing.value = true
  try {
    const formData = new FormData()
    formData.append('file', selectedFile.value)
    importResult.value = await importUserApi(formData)
    activeStep.value = 2
  } catch {
    // 错误提示由请求层统一处理，停留在上传步骤可重试
  } finally {
    importing.value = false
  }
}

/** 是否存在失败明细 */
const hasFailures = () => (importResult.value?.failureRows?.length ?? 0) > 0

/** 完成：关闭对话框并刷新列表 */
function handleFinish(): void {
  visible.value = false
  emit('success')
}

/** 重新导入：回到上传步骤 */
function handleRestart(): void {
  selectedFile.value = null
  importResult.value = null
  uploadRef.value?.clearFiles()
  activeStep.value = 1
}

defineExpose({ open })
</script>

<template>
  <el-dialog
    v-model="visible"
    title="导入用户"
    width="640px"
    :close-on-click-modal="false"
    append-to-body
  >
    <!-- 步骤条 -->
    <el-steps :active="activeStep" align-center finish-status="success" class="import-steps">
      <el-step title="下载模板" />
      <el-step title="上传文件" />
      <el-step title="导入结果" />
    </el-steps>

    <!-- 步骤一：下载模板 -->
    <div v-show="activeStep === 0" class="step-body">
      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="请先下载导入模板，按模板格式填写用户数据（用户名、昵称等必填项不能为空）；密码列留空将使用系统参数 sys.user.initPassword 的初始密码，导入用户首次登录须修改密码"
        class="step-alert"
      />
      <div class="step-actions">
        <el-button
          type="primary"
          :icon="Download"
          :loading="templateDownloading"
          @click="handleDownloadTemplate"
        >
          下载模板
        </el-button>
      </div>
    </div>

    <!-- 步骤二：上传文件 -->
    <div v-show="activeStep === 1" class="step-body">
      <el-upload
        ref="uploadRef"
        drag
        accept=".xlsx"
        :limit="1"
        :auto-upload="false"
        :on-change="handleFileChange"
        :on-remove="handleFileRemove"
        :on-exceed="handleExceed"
      >
        <el-icon :size="40" class="el-icon--upload"><Upload /></el-icon>
        <div class="el-upload__text">将 xlsx 文件拖到此处，或<em>点击选择</em></div>
        <template #tip>
          <div class="el-upload__tip">仅支持 .xlsx 格式，且一次只能上传一个文件</div>
        </template>
      </el-upload>
    </div>

    <!-- 步骤三：结果展示 -->
    <div v-if="activeStep === 2 && importResult" class="step-body">
      <el-alert
        :type="hasFailures() ? 'warning' : 'success'"
        :closable="false"
        show-icon
        :title="`成功导入 ${importResult.successCount} 条用户数据`"
        class="step-alert"
      />
      <template v-if="hasFailures()">
        <div class="failure-title">失败明细（{{ importResult.failureRows.length }} 条）</div>
        <el-table :data="importResult.failureRows" max-height="240" size="small" border>
          <el-table-column label="行号" prop="row" width="80" align="center" />
          <el-table-column label="失败原因" prop="reason" min-width="200" show-overflow-tooltip />
        </el-table>
      </template>
    </div>

    <template #footer>
      <template v-if="activeStep === 0">
        <el-button @click="visible = false">取 消</el-button>
        <el-button type="primary" @click="activeStep = 1">下一步</el-button>
      </template>
      <template v-else-if="activeStep === 1">
        <el-button @click="activeStep = 0">上一步</el-button>
        <el-button
          type="primary"
          :loading="importing"
          :disabled="!selectedFile"
          @click="handleImport"
        >
          开始导入
        </el-button>
      </template>
      <template v-else>
        <el-button v-if="hasFailures()" @click="handleRestart">重新导入</el-button>
        <el-button type="primary" @click="handleFinish">完 成</el-button>
      </template>
    </template>
  </el-dialog>
</template>

<style lang="scss" scoped>
.import-steps {
  margin-bottom: 20px;
}

.step-body {
  padding: 0 8px;
}

.step-alert {
  margin-bottom: 16px;
}

.step-actions {
  text-align: center;
  padding: 12px 0;
}

.failure-title {
  margin: 12px 0 8px;
  font-weight: 600;
}
</style>
