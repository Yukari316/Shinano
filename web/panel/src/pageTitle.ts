import { ref } from 'vue'

/** 页面可覆盖的标题（如插件名）；为 null 时使用路由标题。路由切换时由 App 重置。 */
export const pageTitle = ref<string | null>(null)

/** 标题下方的副标题（如数据更新时间），与标题一样不随内容滚动；为 null 时不显示。路由切换时由 App 重置。 */
export const pageSubtitle = ref<string | null>(null)
