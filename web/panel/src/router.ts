import { createRouter, createWebHistory } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    public?: boolean
    scope?: 'Group' | 'Private'
    /** 页面填满内容区且自身不滚动，由页面内的列表各自滚动。 */
    fill?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('./views/LoginView.vue'), meta: { title: '登录', public: true } },
    { path: '/', name: 'overview', component: () => import('./views/OverviewView.vue'), meta: { title: '总览' } },
    { path: '/bot', name: 'bot', component: () => import('./views/BotView.vue'), meta: { title: 'Bot 状态' } },
    { path: '/plugins', name: 'plugins', component: () => import('./views/PluginsView.vue'), meta: { title: '插件管理' } },
    { path: '/plugins/:id', name: 'plugin', component: () => import('./views/PluginView.vue'), meta: { title: '插件' } },
    { path: '/stats', name: 'stats', component: () => import('./views/StatsView.vue'), meta: { title: '数据统计' } },
    { path: '/chats/group', name: 'chats-group', component: () => import('./views/ChatsView.vue'), meta: { title: '群聊', scope: 'Group' } },
    { path: '/chats/private', name: 'chats-private', component: () => import('./views/ChatsView.vue'), meta: { title: '私聊', scope: 'Private' } },
    { path: '/gaps', name: 'gaps', component: () => import('./views/GapsView.vue'), meta: { title: '归档缺口' } },
    { path: '/logs', name: 'logs', component: () => import('./views/LogsView.vue'), meta: { title: '日志', fill: true } },
    { path: '/config', name: 'config', component: () => import('./views/HostConfigView.vue'), meta: { title: '宿主配置' } },
    { path: '/appearance', name: 'appearance', component: () => import('./views/AppearanceView.vue'), meta: { title: '外观' } },
    { path: '/:pathMatch(.*)*', redirect: '/' }
  ]
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} - Shinano` : 'Shinano'
})

export default router
