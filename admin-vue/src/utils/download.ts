/**
 * 文件下载工具。
 *
 * - 独立 axios 实例：baseURL 同 /api，请求自动附带 token，responseType: 'blob'；
 *   不走 request.ts 的 Result 解包拦截器（二进制流不适用统一响应解包）
 * - 错误响应（含 401）：此时响应体是 Blob，尝试按 JSON 解析后端统一响应的 message 提示，
 *   解析失败时按 HTTP 状态码给出兜底文案
 * - downloadFile：GET 下载（查询参数透传），文件名优先取 Content-Disposition，取不到用默认
 * - downloadPost：POST 下载（FormData 提交）
 */
import axios from 'axios'
import type { AxiosError } from 'axios'
import { ElMessage } from 'element-plus'
import { getAccessToken } from './token'

/** 下载实例（文件传输耗时较长，放宽超时；导出实例供测试注入 adapter） */
export const downloadAxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  timeout: 60_000,
})

// 请求拦截器：注入访问令牌
downloadAxiosInstance.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

/**
 * 从 Content-Disposition 响应头解析文件名。
 *
 * 优先解析 filename*=UTF-8'' 形式（后端中文文件名编码写法），
 * 其次解析 filename="xxx" / filename=xxx；解析不到返回空字符串。
 */
export function parseDispositionFilename(disposition: string | undefined | null): string {
  if (!disposition) {
    return ''
  }
  const utf8Match = disposition.match(/filename\*\s*=\s*UTF-8''([^;]+)/i)
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].trim().replace(/^"|"$/g, ''))
    } catch {
      // encodeURIComponent 反解码失败时按原值返回
      return utf8Match[1].trim().replace(/^"|"$/g, '')
    }
  }
  const plainMatch = disposition.match(/filename\s*=\s*"?([^";]+)"?/i)
  return plainMatch?.[1]?.trim() ?? ''
}

/**
 * 从 Blob 错误响应体解析提示消息（尝试按 JSON 解析后端统一响应的 message）。
 * 非 JSON 或解析不到 message 时返回空字符串。
 */
export async function parseBlobErrorMessage(data: unknown): Promise<string> {
  if (data instanceof Blob) {
    try {
      const text = await data.text()
      const parsed = JSON.parse(text) as { message?: string }
      if (parsed.message) {
        return parsed.message
      }
    } catch {
      // 非 JSON 响应体，忽略走兜底文案
    }
  }
  return ''
}

/** 展示下载错误提示（grouping 合并连续相同提示） */
function showDownloadError(message: string): void {
  ElMessage.error({ message, grouping: true })
}

// 响应拦截器：成功原样返回；错误统一提示（错误体为 Blob，尝试 JSON 解析）
downloadAxiosInstance.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const { response } = error
    if (response) {
      const blobMessage = await parseBlobErrorMessage(response.data)
      if (blobMessage) {
        showDownloadError(blobMessage)
      } else if (response.status === 401) {
        showDownloadError('登录状态已失效，请重新登录')
      } else {
        showDownloadError(`下载失败（${response.status}）`)
      }
      return Promise.reject(error)
    }
    showDownloadError('网络异常，请检查网络连接')
    return Promise.reject(error)
  },
)

/** 通过隐藏链接触发浏览器下载并释放对象 URL */
function saveBlob(blob: Blob, filename: string): void {
  const objectUrl = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = objectUrl
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(objectUrl)
}

/**
 * GET 方式下载文件。
 *
 * @param url 接口地址（不含 baseURL）
 * @param params 查询参数（导出场景透传当前搜索条件）
 * @param defaultFilename 默认文件名（Content-Disposition 取不到时使用）
 */
export async function downloadFile(
  url: string,
  params?: Record<string, unknown>,
  defaultFilename = '下载文件',
): Promise<void> {
  const response = await downloadAxiosInstance.get<Blob>(url, {
    params,
    responseType: 'blob',
  })
  const filename =
    parseDispositionFilename(response.headers['content-disposition'] as string | undefined) ||
    defaultFilename
  saveBlob(response.data, filename)
}

/**
 * POST 方式下载文件（FormData 提交）。
 *
 * @param url 接口地址（不含 baseURL）
 * @param formData 表单数据
 * @param defaultFilename 默认文件名（Content-Disposition 取不到时使用）
 */
export async function downloadPost(
  url: string,
  formData: FormData,
  defaultFilename = '下载文件',
): Promise<void> {
  const response = await downloadAxiosInstance.post<Blob>(url, formData, {
    responseType: 'blob',
  })
  const filename =
    parseDispositionFilename(response.headers['content-disposition'] as string | undefined) ||
    defaultFilename
  saveBlob(response.data, filename)
}
