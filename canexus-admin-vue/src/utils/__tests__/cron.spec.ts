/**
 * Cron 表达式常用格式校验测试（定时任务表单前端校验逻辑）。
 */
import { describe, expect, it } from 'vitest'
import { isValidCronExpression } from '../cron'

describe('Cron 表达式常用格式校验', () => {
  it('合法表达式：5 段与 6 段常用写法均通过', () => {
    expect(isValidCronExpression('0 0/5 * * *')).toBe(true)
    expect(isValidCronExpression('0 0 12 * * ?')).toBe(true)
    expect(isValidCronExpression('0/30 * * * * ?')).toBe(true)
    expect(isValidCronExpression('0 15 10 ? * 1,3,5')).toBe(true)
    expect(isValidCronExpression('0 0 10 1-5 * ?')).toBe(true)
  })

  it('非法表达式：段数不足或含非法字符均不通过', () => {
    expect(isValidCronExpression('')).toBe(false)
    expect(isValidCronExpression('* * * *')).toBe(false)
    expect(isValidCronExpression('* * * * * * *')).toBe(false)
    expect(isValidCronExpression('abc * * * *')).toBe(false)
  })

  it('前后空白不参与校验（自动裁剪）', () => {
    expect(isValidCronExpression('  0 0 12 * * ?  ')).toBe(true)
  })
})
