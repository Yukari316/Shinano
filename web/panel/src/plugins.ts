import { ref } from 'vue'
import { api } from './api'
import type { PluginSnapshot, PluginStatus } from './types'

/** 插件列表，侧边栏与插件页面共用；启停、排序或保存后调用 {@link refreshPlugins}。 */
export const plugins = ref<PluginSnapshot[]>([])

export async function refreshPlugins(): Promise<void> {
  plugins.value = await api.get<PluginSnapshot[]>('/api/plugins')
}

/** 插件运行状态的显示文字与 daisyUI 徽章样式。 */
export const pluginStatusBadges: Record<PluginStatus, { label: string; cls: string }> = {
  Active: { label: '已启用', cls: 'badge-success' },
  Disabled: { label: '已停用', cls: 'badge-ghost' },
  Pending: { label: '等待激活', cls: 'badge-warning' },
  InitFailed: { label: '初始化失败', cls: 'badge-error' },
  LoadFailed: { label: '加载失败', cls: 'badge-error' },
}
