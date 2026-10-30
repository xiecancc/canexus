import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { UserInfo } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'
import { usePermission } from '../usePermission'

/** 构造用户信息 */
function makeUserInfo(partial: Partial<UserInfo>): UserInfo {
  return {
    userId: '00000000-0000-0000-0000-000000000001',
    userName: 'admin',
    nickName: '管理员',
    avatar: '',
    roles: [],
    permissions: [],
    ...partial,
  }
}

describe('usePermission 权限判断', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('超管通配 *:*:* 恒真', () => {
    const authStore = useAuthStore()
    authStore.userInfo = makeUserInfo({ permissions: ['*:*:*'] })
    const { hasPerm, hasPermOr, hasPermAnd } = usePermission()

    expect(hasPerm('any:perm:x')).toBe(true)
    expect(hasPerm(['a:b:c', 'd:e:f'])).toBe(true)
    expect(hasPermOr(['x:y:z'])).toBe(true)
    expect(hasPermAnd(['x:y:z', 'm:n:o'])).toBe(true)
  })

  it('普通用户：字符串与数组任一匹配即通过', () => {
    const authStore = useAuthStore()
    authStore.userInfo = makeUserInfo({
      permissions: ['system:user:list', 'monitor:online:query'],
    })
    const { hasPerm, hasPermOr } = usePermission()

    expect(hasPerm('system:user:list')).toBe(true)
    expect(hasPerm('system:user:add')).toBe(false)
    // 数组：任一匹配即通过
    expect(hasPerm(['system:user:add', 'monitor:online:query'])).toBe(true)
    expect(hasPerm(['system:user:add', 'system:user:remove'])).toBe(false)
    expect(hasPermOr(['a:b:c'])).toBe(false)
  })

  it('hasPermAnd 要求全部权限满足', () => {
    const authStore = useAuthStore()
    authStore.userInfo = makeUserInfo({ permissions: ['system:user:list'] })
    const { hasPermAnd } = usePermission()

    expect(hasPermAnd(['system:user:list'])).toBe(true)
    expect(hasPermAnd(['system:user:list', 'system:user:add'])).toBe(false)
  })

  it('hasRole 任一角色匹配即通过', () => {
    const authStore = useAuthStore()
    authStore.userInfo = makeUserInfo({ roles: ['admin', 'common'] })
    const { hasRole } = usePermission()

    expect(hasRole('admin')).toBe(true)
    expect(hasRole('guest')).toBe(false)
    expect(hasRole(['guest', 'common'])).toBe(true)
  })

  it('未加载用户信息时全部拒绝', () => {
    const { hasPerm, hasRole } = usePermission()

    expect(hasPerm('a:b:c')).toBe(false)
    expect(hasRole('admin')).toBe(false)
  })
})
