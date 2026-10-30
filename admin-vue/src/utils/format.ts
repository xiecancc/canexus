/**
 * 通用格式化工具。
 */

/** 将时间值格式化为 YYYY-MM-DD HH:mm:ss；空值或非法值返回空字符串 */
export function formatDateTime(value: string | number | Date | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return ''
  }
  let date: Date
  if (value instanceof Date) {
    date = value
  } else if (typeof value === 'number') {
    date = new Date(value)
  } else {
    // 兼容 "YYYY-MM-DD HH:mm:ss" 形式（部分环境不支持空格分隔的日期字符串解析）
    date = new Date(value.includes('T') ? value : value.replace(' ', 'T'))
  }
  if (Number.isNaN(date.getTime())) {
    return ''
  }
  const pad = (num: number): string => String(num).padStart(2, '0')
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
  )
}
