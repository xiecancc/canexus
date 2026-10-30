/**
 * 文件下载工具测试：纯逻辑（文件名解析 / Blob 错误消息解析）+ 下载流程
 * （注入 axios adapter，stub 对象 URL 与链接触发，验证参数透传与错误提示）。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('element-plus', () => ({
  ElMessage: {
    error: vi.fn(),
    success: vi.fn(),
    warning: vi.fn(),
  },
}))

import { ElMessage } from 'element-plus'
import {
  downloadAxiosInstance,
  downloadFile,
  downloadPost,
  parseBlobErrorMessage,
  parseDispositionFilename,
} from '../download'

/** 记录 adapter 收到的请求配置（method / url / params / data） */
let lastRequestConfig: {
  method?: string
  url?: string
  params?: Record<string, unknown>
  data?: unknown
  responseType?: string
} = {}

/** 注入假 adapter：按给定响应（或错误）应答 */
function stubAdapter(response: { status: number; body: Blob; headers?: Record<string, string> }) {
  downloadAxiosInstance.defaults.adapter = async (config) => {
    lastRequestConfig = {
      method: config.method,
      url: config.url,
      params: config.params as Record<string, unknown> | undefined,
      data: config.data,
      responseType: config.responseType,
    }
    if (response.status >= 400) {
      const error = new Error('Request failed') as Error & {
        response: { status: number; data: Blob; headers: Record<string, string> }
      }
      error.response = {
        status: response.status,
        data: response.body,
        headers: response.headers ?? {},
      }
      throw error
    }
    return {
      data: response.body,
      status: response.status,
      statusText: 'OK',
      headers: response.headers ?? {},
      config,
    }
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  lastRequestConfig = {}
  // jsdom 未实现对象 URL，stub 记录调用
  URL.createObjectURL = vi.fn(() => 'blob:mock')
  URL.revokeObjectURL = vi.fn()
})

describe('Content-Disposition 文件名解析', () => {
  it("优先解析 filename*=UTF-8'' 编码文件名", () => {
    const disposition = "attachment; filename*=UTF-8''%E7%94%A8%E6%88%B7%E6%95%B0%E6%8D%AE.xlsx"
    expect(parseDispositionFilename(disposition)).toBe('用户数据.xlsx')
  })

  it('解析普通 filename="xxx"，空头返回空字符串', () => {
    expect(parseDispositionFilename('attachment; filename="users.xlsx"')).toBe('users.xlsx')
    expect(parseDispositionFilename(undefined)).toBe('')
  })
})

describe('Blob 错误消息解析', () => {
  it('JSON 错误体解析出后端 message', async () => {
    const blob = new Blob([JSON.stringify({ code: 401, message: '登录状态已失效' })])
    await expect(parseBlobErrorMessage(blob)).resolves.toBe('登录状态已失效')
  })

  it('非 JSON 错误体返回空字符串', async () => {
    await expect(parseBlobErrorMessage(new Blob(['plain text']))).resolves.toBe('')
  })
})

describe('下载流程', () => {
  it('downloadFile：GET blob 请求、查询参数透传、按响应头文件名触发下载', async () => {
    const blob = new Blob(['xlsx-bytes'])
    stubAdapter({
      status: 200,
      body: blob,
      headers: { 'content-disposition': `attachment; filename*=UTF-8''%E7%94%A8%E6%88%B7.xlsx` },
    })
    await downloadFile('/system/user/export', { keyword: '张', status: 1 }, '用户数据.xlsx')

    expect(lastRequestConfig.method).toBe('get')
    expect(lastRequestConfig.url).toBe('/system/user/export')
    expect(lastRequestConfig.params).toEqual({ keyword: '张', status: 1 })
    expect(lastRequestConfig.responseType).toBe('blob')
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock')
  })

  it('downloadFile：401 且错误体为 JSON Blob 时提示后端 message', async () => {
    stubAdapter({
      status: 401,
      body: new Blob([JSON.stringify({ code: 401, message: '令牌已过期' })]),
    })
    await expect(
      downloadFile('/system/user/export', undefined, '用户数据.xlsx'),
    ).rejects.toBeDefined()
    expect(ElMessage.error).toHaveBeenCalledWith({ message: '令牌已过期', grouping: true })
  })

  it('downloadFile：Content-Disposition 缺失时回退默认文件名（不抛错）', async () => {
    stubAdapter({ status: 200, body: new Blob(['xlsx-bytes']) })
    await expect(
      downloadFile('/system/user/export', undefined, '用户数据.xlsx'),
    ).resolves.toBeUndefined()
  })

  it('downloadPost：POST blob 请求且 FormData 原样透传', async () => {
    stubAdapter({ status: 200, body: new Blob(['xlsx-bytes']) })
    const formData = new FormData()
    formData.append('file', new File(['demo'], 'users.xlsx'))
    await downloadPost('/system/user/export-by-form', formData, '导出.xlsx')

    expect(lastRequestConfig.method).toBe('post')
    expect(lastRequestConfig.data).toBe(formData)
    expect(lastRequestConfig.responseType).toBe('blob')
  })
})
