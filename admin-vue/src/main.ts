import { createApp } from 'vue'
import { createPinia } from 'pinia'

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

app.mount('#app')
