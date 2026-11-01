/**
 * commitStatusToggle 状态切换纯逻辑测试：
 * 模拟 el-switch 已将新状态写入行数据后提交，验证失败回滚与结果返回。
 */
import { describe, expect, it, vi } from 'vitest'
import { commitStatusToggle } from '../useStatusToggle'

describe('commitStatusToggle 状态切换', () => {
  it('提交成功：保持 switch 已写入的新状态并返回 true', async () => {
    // 初始为停用(0)，switch 已切换为启用(1)
    const row = { status: 1 }
    const submit = vi.fn().mockResolvedValue(null)

    const result = await commitStatusToggle(row, submit)

    expect(result).toBe(true)
    expect(submit).toHaveBeenCalledWith(1)
    expect(row.status).toBe(1)
  })

  it('提交失败：回滚为切换前状态（停用→启用失败回到停用）', async () => {
    const row = { status: 1 }
    const submit = vi.fn().mockRejectedValue(new Error('请求失败'))

    const result = await commitStatusToggle(row, submit)

    expect(result).toBe(false)
    expect(row.status).toBe(0)
  })

  it('提交失败：回滚为切换前状态（启用→停用失败回到启用）', async () => {
    const row = { status: 0 }
    const result = await commitStatusToggle(row, () => Promise.reject(new Error('请求失败')))
    expect(result).toBe(false)
    expect(row.status).toBe(1)
  })

  it('提交函数收到的是 switch 已写入的新状态', async () => {
    const submit = vi.fn().mockResolvedValue(null)
    const row = { status: 0 }
    await commitStatusToggle(row, submit)
    expect(submit).toHaveBeenCalledTimes(1)
    expect(submit).toHaveBeenCalledWith(0)
  })
})
