import { computed, ref } from 'vue'
import { api } from './api'
import type { Appearance } from './types'

/**
 * 部署级外观设置；背景图地址带版本号，替换图片后浏览器重新加载。
 * 只有背景图本身变化时才递增版本号：地址一变浏览器就会重新下载，下载完成前没有背景，整页会闪烁。
 */
export const appearance = ref<Appearance>({ panelOpacity: 80, cardOpacity: 80, veilOpacity: 40, blur: false, backgroundFile: null })
// 初始值取页面加载时刻，每次打开页面都取当前的背景图，不会命中其他管理员替换前的缓存。
const backgroundVersion = ref(Date.now())

export const backgroundUrl = computed(() =>
  appearance.value.backgroundFile ? `/api/appearance/background?v=${backgroundVersion.value}` : null
)

export async function refreshAppearance(backgroundChanged = false): Promise<void> {
  appearance.value = await api.get<Appearance>('/api/appearance')
  if (backgroundChanged) backgroundVersion.value++
}
