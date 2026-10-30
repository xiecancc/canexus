import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { ElMessage } from 'element-plus'

import App from './App.vue'
import router from './router'
import permission from './directives/permission'

// Element Plus 暗黑模式变量（须在自定义样式之前引入，保证覆盖顺序）
import 'element-plus/theme-chalk/dark/css-vars.css'
import '@/styles/index.scss'

const app = createApp(App)

app.use(createPinia())
app.use(router)

// 按钮级权限指令：v-permission
app.directive('permission', permission)

// 全局错误兜底：组件内未捕获的异常统一记录并提示（接前端监控上报时在此扩展）
app.config.errorHandler = (err, _instance, info) => {
  console.error(`[app] 未处理异常（${info}）：`, err)
  ElMessage.error({ message: '页面发生异常，请重试或刷新页面', grouping: true })
}

app.mount('#app')
