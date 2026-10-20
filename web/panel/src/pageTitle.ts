import { ref } from 'vue'

/** 页面可覆盖的标题（如插件名）；为 null 时使用路由标题。路由切换时由 App 重置。 */
export const pageTitle = ref<string | null>(null)
