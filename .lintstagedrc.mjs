/**
 * 使用函数形式而非文件列表拼接，原因：
 * 1. 大规模暂存（如目录更名）会把数百个文件路径拼进命令行，
 *    超出 Windows 32767 字符上限导致钩子被杀（--include 与 argv 同理）；
 * 2. 函数形式不追加文件参数，改为对子项目全量检查——代码库保持全绿时代价仅毫秒/秒级。
 */
/** @type {import('lint-staged').Configuration} */
const config = {
  '*.{js,mjs,cjs,ts,vue}': () => [
    'pnpm -C canexus-admin-vue exec oxlint --fix src e2e',
    'pnpm -C canexus-admin-vue exec oxfmt src e2e',
  ],
  // 写模式而非 --verify-no-changes：verify 的 ENDOFLINE 检查在 Windows（core.autocrlf=true +
  // lint-staged 经 git 管道命令备份/还原工作树，行尾回退为 LF）下会误报并死锁提交；
  // 写模式就地修复行尾/编码/格式，lint-staged 自动暂存修复结果，门禁语义不变。
  '*.cs': () => 'dotnet format canexus-api-netcore/api.slnx',
}

export default config
