/**
 * 行内状态切换纯逻辑：供用户 / 角色等列表页的状态开关复用。
 *
 * el-switch 通过 v-model 绑定行数据，change 触发时行状态已是新值；
 * 提交失败时回滚为原状态（响应式变更会同步恢复开关展示），
 * 错误提示由请求层统一处理，此处只负责状态回滚与结果返回。
 */

/**
 * 提交状态切换：成功保持新状态并返回 true，失败回滚为原状态并返回 false。
 *
 * @param row    行数据（需含二值 status 字段：1 启用 0 停用）
 * @param submit 提交函数，入参为待提交的新状态
 */
export async function commitStatusToggle<T extends { status: number }>(
  row: T,
  submit: (status: number) => Promise<unknown>,
): Promise<boolean> {
  const prevStatus = row.status === 1 ? 0 : 1
  try {
    await submit(row.status)
    return true
  } catch {
    row.status = prevStatus
    return false
  }
}
