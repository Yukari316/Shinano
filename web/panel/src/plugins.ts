import { ref } from 'vue'
import { api } from './api'
import type { PluginSnapshot } from './types'

/** 插件列表，侧边栏与插件页面共用；启停、排序或保存后调用 {@link refreshPlugins}。 */
export const plugins = ref<PluginSnapshot[]>([])

export async function refreshPlugins(): Promise<void> {
  plugins.value = await api.get<PluginSnapshot[]>('/api/plugins')
}
