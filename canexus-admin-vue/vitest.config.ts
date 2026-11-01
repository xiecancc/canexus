import { fileURLToPath } from 'node:url'
import { mergeConfig, defineConfig, configDefaults } from 'vitest/config'
import viteConfig from './vite.config.ts'

// vite.config.ts 导出的是配置工厂，此处以 test 模式求值后合并
const baseConfig = await viteConfig({ command: 'serve', mode: 'test' })

export default mergeConfig(
  baseConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      // 内联 element-plus，使其样式导入经 vite 处理（css 默认替换为空模块）
      server: {
        deps: {
          inline: [/element-plus/],
        },
      },
      exclude: [...configDefaults.exclude, 'e2e/**'],
      root: fileURLToPath(new URL('./', import.meta.url)),
    },
  }),
)
