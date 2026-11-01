/**
 * Cron 表达式常用格式校验（纯逻辑，供定时任务表单复用与单测）。
 *
 * 仅做前端常用格式校验：5 段（分 时 日 月 周）或 6 段（秒 分 时 日 月 周），
 * 支持 * ? / , - 数字组合；最终合法性以后端校验为准。
 */

/**
 * 单段合法取值：星号、问号、数字、范围（a-b）、步进（a/b 与 星号/b）、列表（a,b,c）及其组合。
 */
const CRON_SEGMENT_PATTERN = /^(\*|\?|\d+(-\d+)?(\/\d+)?|\*\/\d+|\d+(,\d+)+)$/

/**
 * 校验 Cron 表达式是否为常用合法格式。
 *
 * @param expression Cron 表达式（允许前后空白）
 * @returns 是否通过常用格式校验
 */
export function isValidCronExpression(expression: string): boolean {
  const trimmed = expression.trim()
  if (!trimmed) {
    return false
  }
  const segments = trimmed.split(/\s+/)
  if (segments.length < 5 || segments.length > 6) {
    return false
  }
  return segments.every((segment) => CRON_SEGMENT_PATTERN.test(segment))
}
