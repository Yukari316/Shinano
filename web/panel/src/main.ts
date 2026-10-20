import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import './style.css'

const app = createApp(App).use(router)
// 等首次导航解析完成再挂载，否则直接打开登录页时会按非公开页面先请求数据。
router.isReady().then(() => app.mount('#app'))
