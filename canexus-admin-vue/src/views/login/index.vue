<script setup lang="ts">
/**
 * 登录页：账号 + 密码 + 图形验证码，支持记住账号、回车提交与点击刷新验证码。
 * 布局为左侧品牌区（渐变背景）+ 右侧表单卡片，移动端隐藏品牌区，适配暗黑模式。
 */
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { Key, Lock, Platform, User } from '@element-plus/icons-vue'
import type { CaptchaResponse } from '@/api/auth'
import { getCaptchaApi } from '@/api/auth'
import { CHANGE_PASSWORD_ROUTE_PATH } from '@/router/constants'
import { useAuthStore } from '@/stores/auth'

/** 记住账号存储键 */
const REMEMBER_KEY = 'super_admin_remember_username'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const formRef = ref<FormInstance>()

/** 登录提交中 */
const loading = ref(false)

/** 验证码信息 */
const captcha = ref<CaptchaResponse>({ captchaId: '', imageBase64: '' })

const form = reactive({
  userName: '',
  password: '',
  captchaCode: '',
  /** 是否记住账号 */
  remember: false,
})

/** 表单校验规则（用户名/密码必填、验证码 4 位） */
const rules: FormRules = {
  userName: [{ required: true, message: '请输入账号', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }],
  captchaCode: [
    { required: true, message: '请输入验证码', trigger: 'blur' },
    { min: 4, max: 4, message: '验证码为 4 位字符', trigger: 'blur' },
  ],
}

/** 获取验证码（点击图片可重新获取） */
async function refreshCaptcha(): Promise<void> {
  try {
    captcha.value = await getCaptchaApi()
    form.captchaCode = ''
  } catch {
    // 失败提示已由请求层统一处理，保持占位图可点击重试
  }
}

/** 登录提交 */
async function handleLogin(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  loading.value = true
  try {
    const result = await authStore.login({
      userName: form.userName.trim(),
      password: form.password,
      captchaId: captcha.value.captchaId,
      captchaCode: form.captchaCode.trim(),
    })
    // 记住账号（仅记住账号名，不保存密码）
    if (form.remember) {
      localStorage.setItem(REMEMBER_KEY, form.userName.trim())
    } else {
      localStorage.removeItem(REMEMBER_KEY)
    }
    // 后端返回强制改密标记：受保护端点会被 40302 拦截，先引导改密再进入后台
    if (result.mustChangePassword === true) {
      ElMessage.warning('首次登录须先修改初始密码')
      router.push(CHANGE_PASSWORD_ROUTE_PATH)
      return
    }
    ElMessage.success(`欢迎回来，${form.userName.trim()}`)
    router.push(resolveRedirectTarget())
  } catch {
    // 登录失败（账号 / 验证码错误）：刷新验证码便于重试
    await refreshCaptcha()
  } finally {
    loading.value = false
  }
}

/** 解析回跳地址：仅允许站内路径，防外链跳转 */
function resolveRedirectTarget(): string {
  const redirect = route.query.redirect
  if (typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//')) {
    return redirect
  }
  return '/'
}

onMounted(() => {
  // 回填记住的账号
  const remembered = localStorage.getItem(REMEMBER_KEY)
  if (remembered) {
    form.userName = remembered
    form.remember = true
  }
  refreshCaptcha()
})
</script>

<template>
  <div class="login-page">
    <!-- 左侧品牌区 -->
    <section class="login-brand">
      <div class="brand-content">
        <div class="brand-logo">
          <AppIcon name="Platform" :size="40" />
        </div>
        <h1 class="brand-name">Super Admin</h1>
        <p class="brand-slogan">对标若依重构的后台管理框架基座</p>
        <ul class="brand-features">
          <li>
            <AppIcon name="Aim" :size="16" />
            <span>动态路由 · 按钮级权限</span>
          </li>
          <li>
            <AppIcon name="MagicStick" :size="16" />
            <span>暗黑模式 · 主题定制</span>
          </li>
          <li>
            <AppIcon name="Promotion" :size="16" />
            <span>Vite 8 · Vue 3 · TypeScript</span>
          </li>
        </ul>
      </div>
    </section>

    <!-- 右侧表单卡片 -->
    <section class="login-panel">
      <div class="login-card">
        <h2 class="login-title">欢迎登录</h2>
        <p class="login-subtitle">请输入账号信息进入后台管理系统</p>
        <el-form ref="formRef" :model="form" :rules="rules" size="large" @keyup.enter="handleLogin">
          <el-form-item prop="userName">
            <el-input
              v-model="form.userName"
              placeholder="账号"
              :prefix-icon="User"
              autocomplete="username"
              clearable
            />
          </el-form-item>
          <el-form-item prop="password">
            <el-input
              v-model="form.password"
              type="password"
              placeholder="密码"
              :prefix-icon="Lock"
              autocomplete="current-password"
              show-password
            />
          </el-form-item>
          <el-form-item prop="captchaCode">
            <div class="captcha-row">
              <el-input
                v-model="form.captchaCode"
                placeholder="验证码"
                :prefix-icon="Key"
                maxlength="4"
                clearable
              />
              <img
                v-if="captcha.imageBase64"
                :src="captcha.imageBase64"
                alt="验证码"
                title="点击刷新验证码"
                class="captcha-image"
                @click="refreshCaptcha"
              />
              <div v-else class="captcha-image captcha-placeholder" @click="refreshCaptcha">
                点击获取
              </div>
            </div>
          </el-form-item>
          <el-form-item>
            <el-checkbox v-model="form.remember">记住账号</el-checkbox>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" class="login-button" :loading="loading" @click="handleLogin">
              登 录
            </el-button>
          </el-form-item>
        </el-form>
      </div>
    </section>
  </div>
</template>

<style lang="scss" scoped>
.login-page {
  display: flex;
  min-height: 100vh;
}

/* 左侧品牌区：渐变背景，暗黑模式下加深 */
.login-brand {
  display: flex;
  flex: 1 1 55%;
  align-items: center;
  justify-content: center;
  padding: 32px;
  color: #ffffff;
  background: linear-gradient(135deg, #1d39c4 0%, #1677ff 55%, #36cfc9 100%);

  html.dark & {
    background: linear-gradient(135deg, #0b1a4a 0%, #0c2d5c 55%, #0f4a48 100%);
  }
}

.brand-content {
  max-width: 420px;
}

.brand-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64px;
  height: 64px;
  margin-bottom: 20px;
  background: rgba(255, 255, 255, 0.16);
  border-radius: 16px;
}

.brand-name {
  margin: 0 0 8px;
  font-size: 30px;
  font-weight: 700;
  letter-spacing: 1px;
}

.brand-slogan {
  margin: 0 0 32px;
  font-size: 15px;
  opacity: 0.85;
}

.brand-features {
  display: flex;
  flex-direction: column;
  gap: 14px;
  margin: 0;
  padding: 0;
  font-size: 14px;
  list-style: none;

  li {
    display: flex;
    align-items: center;
    gap: 8px;
    opacity: 0.9;
  }
}

/* 右侧表单区 */
.login-panel {
  display: flex;
  flex: 1 1 45%;
  align-items: center;
  justify-content: center;
  padding: 24px;
  background-color: var(--el-bg-color-page);
}

.login-card {
  width: 100%;
  max-width: 400px;
  padding: 36px 32px;
  background-color: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  box-shadow: var(--el-box-shadow-light);
}

.login-title {
  margin: 0 0 6px;
  font-size: 22px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.login-subtitle {
  margin: 0 0 24px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

/* 验证码行：输入框 + 图片 */
.captcha-row {
  display: flex;
  gap: 10px;
  width: 100%;

  .el-input {
    flex: 1;
  }
}

.captcha-image {
  flex-shrink: 0;
  width: 110px;
  height: 40px;
  cursor: pointer;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
}

.captcha-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
  background-color: var(--el-fill-color-light);
}

.login-button {
  width: 100%;
}

/* 移动端：隐藏品牌区 */
@media (max-width: 768px) {
  .login-brand {
    display: none;
  }
}
</style>
