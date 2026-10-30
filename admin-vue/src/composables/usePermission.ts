/**
 * 权限判断组合式函数。
 *
 * 完全由后端下发的 roles / permissions 数据驱动，不写死任何菜单或权限名；
 * 权限标识为 ["*:*:*"] 时视为超级管理员，恒通过。
 */
import { useAuthStore } from '@/stores/auth'

/** 超级管理员通配权限标识 */
export const SUPER_ADMIN_PERM = '*:*:*'

/** 权限判断组合式函数 */
export function usePermission() {
  const authStore = useAuthStore()

  /**
   * 是否拥有指定权限：传入字符串或数组（任一匹配即通过）；超管通配恒真。
   */
  function hasPerm(perm: string | string[]): boolean {
    const permissions = authStore.permissions
    if (permissions.includes(SUPER_ADMIN_PERM)) {
      return true
    }
    const required = Array.isArray(perm) ? perm : [perm]
    return required.some((item) => permissions.includes(item))
  }

  /**
   * 是否拥有指定角色：传入字符串或数组（任一匹配即通过）。
   */
  function hasRole(role: string | string[]): boolean {
    const roles = authStore.roles
    const required = Array.isArray(role) ? role : [role]
    return required.some((item) => roles.includes(item))
  }

  /**
   * 任一权限满足即通过。
   */
  function hasPermOr(perms: string[]): boolean {
    return hasPerm(perms)
  }

  /**
   * 全部权限满足才通过（超管通配恒真）。
   */
  function hasPermAnd(perms: string[]): boolean {
    const permissions = authStore.permissions
    if (permissions.includes(SUPER_ADMIN_PERM)) {
      return true
    }
    return perms.every((item) => permissions.includes(item))
  }

  return { hasPerm, hasRole, hasPermOr, hasPermAnd }
}
