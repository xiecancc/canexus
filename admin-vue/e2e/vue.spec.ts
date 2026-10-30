import { test, expect } from '@playwright/test'

// 冒烟：未登录访问首页应被路由守卫重定向到登录页
test('visits the app root url', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('button', { name: '登 录' })).toBeVisible()
})
