/**
 * 认证模块接口与后端契约类型。
 *
 * 契约约定（与后端 AuthController / TokenPairDto 对齐）：
 * - GET  /auth/captcha         → { captchaId, imageBase64 }
 * - POST /auth/login           → { accessToken, refreshToken, expiresIn, mustChangePassword }
 * - POST /auth/refresh         → 同上（由 utils/request 静默刷新使用）
 * - POST /auth/change-password → null（改密后后端吊销全部刷新令牌，须重新登录）
 * - POST /auth/logout          → null
 * - GET  /auth/user-info       → { userId, userName, nickName, avatar, roles, permissions }
 * - GET  /auth/routes          → RouteVO[]
 *
 * 待改密用户（mustChangePassword=true）访问受保护端点会被中间件以 HTTP 403 / 业务码 40302 拦截，
 * 仅自助改密、用户信息、路由与登出端点放行，故前端必须提供改密入口并据此引导。
 */
import request from '@/utils/request'

/** 登录请求参数 */
export interface LoginParams {
  /** 账号 */
  userName: string
  /** 密码 */
  password: string
  /** 验证码标识 */
  captchaId: string
  /** 验证码输入值 */
  captchaCode: string
}

/** 登录 / 刷新令牌响应 */
export interface LoginResponse {
  /** 访问令牌 */
  accessToken: string
  /** 刷新令牌 */
  refreshToken: string
  /** 访问令牌有效期（秒；由后端 Jwt:AccessTokenMinutes 决定，当前默认 30 分钟 = 1800 秒） */
  expiresIn: number
  /** 是否须先修改初始密码（true 时受保护端点会被 40302 拦截） */
  mustChangePassword: boolean
}

/** 自助修改密码请求参数 */
export interface ChangePasswordParams {
  /** 原密码（首次强制改密时为初始密码） */
  oldPassword: string
  /** 新密码（6-20 位且同时包含字母与数字） */
  newPassword: string
}

/** 图形验证码响应 */
export interface CaptchaResponse {
  /** 验证码标识（登录时回传） */
  captchaId: string
  /** 验证码图片（data URI，可直接作为 img src） */
  imageBase64: string
}

/** 当前登录用户信息 */
export interface UserInfo {
  /** 用户 ID（GUID 字符串） */
  userId: string
  /** 账号 */
  userName: string
  /** 昵称 */
  nickName: string
  /** 头像地址（可为空字符串） */
  avatar: string
  /** 角色标识集合 */
  roles: string[]
  /** 权限标识集合（超管为 ["*:*:*"]） */
  permissions: string[]
}

/** 动态路由元信息（与后端契约一致，透传到路由 meta） */
export interface RouteMetaVO {
  /** 标题（菜单 / 标签页 / 面包屑显示） */
  title: string
  /** 图标名称 */
  icon?: string
  /** 是否缓存页面 */
  keepAlive?: boolean
  /** 是否在菜单中隐藏 */
  hidden?: boolean
  /** 排序权重（越小越靠前） */
  order?: number
  /** 路由级权限标识（扩展字段，存在时任一匹配即可访问） */
  perms?: string[]
}

/** 动态路由 VO（后端 /auth/routes 返回结构） */
export interface RouteVO {
  /** 路由名（缺失时前端按 path 生成） */
  name?: string
  /** 路径（顶级一般为 /xxx，子级为相对路径） */
  path: string
  /** 组件路径（相对 src/views，如 system/user；Layout 表示目录占位；无组件且无子级视为按钮） */
  component?: string
  /** 重定向地址 */
  redirect?: string
  /** 路由元信息 */
  meta: RouteMetaVO
  /** 子路由 */
  children?: RouteVO[]
}

/** 获取图形验证码 */
export function getCaptchaApi(): Promise<CaptchaResponse> {
  return request.get<CaptchaResponse>('/auth/captcha')
}

/** 账号密码登录 */
export function loginApi(data: LoginParams): Promise<LoginResponse> {
  return request.post<LoginResponse>('/auth/login', data)
}

/** 自助修改密码（改密成功后后端吊销全部刷新令牌，前端须清理本地状态并重新登录） */
export function changePasswordApi(data: ChangePasswordParams): Promise<boolean> {
  return request.post<boolean>('/auth/change-password', data)
}

/** 退出登录（需携带认证头） */
export function logoutApi(): Promise<null> {
  return request.post<null>('/auth/logout')
}

/** 获取当前登录用户信息 */
export function getUserInfoApi(): Promise<UserInfo> {
  return request.get<UserInfo>('/auth/user-info')
}

/** 获取当前用户的动态路由表 */
export function getRoutesApi(): Promise<RouteVO[]> {
  return request.get<RouteVO[]>('/auth/routes')
}
