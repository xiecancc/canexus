/**
 * T12.2 全链路 E2E 冒烟（真实后端 http://localhost:5000，前端 dev http://localhost:5173）：
 * 登录（从验证码 SVG 按字符 x 坐标还原验证码）→ 动态菜单渲染 →
 * 用户管理 CRUD（新增→搜索→删除）→ 字典管理（类型→数据切换）→ 日志查询。
 *
 * 运行前提：后端与前端 dev server 均已启动（playwright.config webServer 复用 5173）。
 */
import { test, expect, type Page } from '@playwright/test'

test.use({ headless: true })

/** 后端种子超管账号 */
const ADMIN_USER = 'admin'
const ADMIN_PASSWORD = 'admin123'

/** 从登录页自身展示的验证码 SVG（img src data URI）中按字符 x 坐标还原明文（后端字符横向均分布局） */
async function resolveCaptchaCode(page: Page): Promise<string> {
  const imageBase64 = await page.locator('img[alt="验证码"]').getAttribute('src')
  expect(imageBase64).toContain('data:image/svg+xml;base64,')
  const svg = Buffer.from(imageBase64!.replace('data:image/svg+xml;base64,', ''), 'base64').toString('utf-8')
  const chars = [...svg.matchAll(/<text x="([\d.]+)"[^>]*>([^<]+)<\/text>/g)]
    .map((m) => ({ x: Number(m[1]), char: m[2] }))
    .sort((a, b) => a.x - b.x)
    .map((item) => item.char)
    .join('')
  expect(chars).toHaveLength(4)
  return chars
}

/** 本次运行唯一的新增用户名（避免重复执行时与库中历史数据冲突） */
const NEW_USER = `e2e_smoke_${Date.now().toString(36)}`

test('全链路冒烟：登录 → 菜单 → 用户 CRUD → 字典 → 日志', async ({ page }) => {
  test.setTimeout(120_000)

  // ---------- 1. 登录 ----------
  await page.goto('/')
  await expect(page).toHaveURL(/\/login/)

  const captchaCode = await resolveCaptchaCode(page)
  await page.getByPlaceholder('账号').fill(ADMIN_USER)
  await page.getByPlaceholder('密码').fill(ADMIN_PASSWORD)
  await page.getByPlaceholder('验证码').fill(captchaCode)
  await page.getByRole('button', { name: '登 录' }).click()

  // 登录成功进入后台布局（动态路由注册完成）
  await expect(page.locator('.welcome-card')).toBeVisible({ timeout: 15_000 })

  // ---------- 2. 动态菜单渲染 ----------
  // （单可见子级的目录直接渲染该子级——系统工具目录展示为"定时任务"菜单项）
  const sidebar = page.locator('.el-menu')
  await expect(sidebar.getByText('系统管理')).toBeVisible()
  await expect(sidebar.getByText('监控')).toBeVisible()
  await expect(sidebar.getByText('定时任务')).toBeVisible()

  // ---------- 3. 用户管理 CRUD ----------
  await page.goto('/system/user')
  await expect(page.getByRole('button', { name: '新增' })).toBeVisible()

  // 3.1 新增用户
  await page.getByRole('button', { name: '新增' }).click()
  const dialog = page.locator('.el-dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByPlaceholder('请输入账号').fill(NEW_USER)
  await dialog.getByPlaceholder('请输入昵称').fill('E2E冒烟用户')
  await dialog.getByRole('button', { name: '确 定' }).click()
  await expect(page.locator('.el-message--success').filter({ hasText: '新增成功' })).toBeVisible()

  // 3.2 搜索新用户并确认出现在列表
  await page.getByPlaceholder('用户名 / 昵称 / 手机号').fill(NEW_USER)
  await page.getByRole('button', { name: '搜索' }).click()
  await expect(page.locator('.el-table').getByText(NEW_USER)).toBeVisible()

  // 3.3 删除该用户（"更多"下拉 → 删除 → 确认弹窗）
  const row = page.locator('.el-table__row').filter({ hasText: NEW_USER })
  await row.getByRole('button', { name: '更多' }).click()
  // 行内"更多"下拉（按菜单内容过滤，与顶栏主题/用户下拉区分）
  const dropdownMenu = page.locator('.el-dropdown-menu', { hasText: '分配角色' })
  await expect(dropdownMenu).toBeVisible()
  await dropdownMenu.getByText('删除', { exact: true }).click()
  await page.locator('.el-message-box').getByRole('button', { name: '确 定' }).click()
  await expect(page.locator('.el-message--success').filter({ hasText: '删除成功' })).toBeVisible()

  // ---------- 4. 字典管理：类型 → 数据切换 ----------
  await page.goto('/system/dict')
  const dictRow = page.locator('.el-table__row').filter({ hasText: 'sys_notice_type' })
  await expect(dictRow).toBeVisible()
  await dictRow.getByRole('button', { name: '字典数据' }).click()
  await expect(page).toHaveURL(/\/system\/dict\/data/, { timeout: 15_000 })
  // 该字典类型的数据项（通知 / 公告）正确加载（同一文案在标签与文本列各出现一次，取首个断言）
  await expect(page.locator('.el-table').getByText('通知', { exact: true }).first()).toBeVisible()
  await expect(page.locator('.el-table').getByText('公告', { exact: true }).first()).toBeVisible()

  // ---------- 5. 日志查询 ----------
  // 5.1 登录日志：应包含本次 admin 登录记录
  await page.goto('/monitor/logininfor')
  await expect(page.locator('.el-table')).toBeVisible()
  await expect(page.locator('.el-table').getByText(ADMIN_USER).first()).toBeVisible()

  // 5.2 操作日志：页面正常加载且可分页查询（新增/删除用户操作已写入操作日志）
  await page.goto('/monitor/operlog')
  await expect(page.locator('.el-table')).toBeVisible()
  await expect(page.locator('.el-pagination')).toBeVisible()
})
