import { relative, sep } from 'node:path'

/**
 * 将 lint-staged 传入的绝对路径转换为仓库根相对路径。
 * 原因：dotnet format 的 --include 不识别绝对路径（静默跳过，不报错）。
 */
function toRepoRelative(filenames) {
  return filenames.map((f) => relative(process.cwd(), f).split(sep).join('/'))
}

/** @type {import('lint-staged').Configuration} */
const config = {
  '*.{js,mjs,cjs,ts,vue}': ['pnpm -C admin-vue exec oxlint --fix', 'pnpm -C admin-vue exec oxfmt'],
  '*.cs': (filenames) => {
    const includes = toRepoRelative(filenames)
      .map((f) => `"${f}"`)
      .join(' ')
    return [`dotnet format api-netcore/api.slnx --include ${includes}`]
  },
}

export default config
