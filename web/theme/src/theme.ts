import { ref } from 'vue'

/** 亮色主题。 */
export const LIGHT_THEME = 'emerald'
/** 暗色主题。 */
export const DARK_THEME = 'dim'

const STORAGE_KEY = 'shinano-theme'

function initialTheme(): string {
  const applied = document.documentElement.dataset.theme
  if (applied === LIGHT_THEME || applied === DARK_THEME) return applied
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? DARK_THEME : LIGHT_THEME
}

/** 当前主题；选择保存在本浏览器，读写失败时只影响记忆，不影响切换。 */
export const theme = ref(initialTheme())

export function toggleTheme(): void {
  theme.value = theme.value === DARK_THEME ? LIGHT_THEME : DARK_THEME
  document.documentElement.dataset.theme = theme.value
  try {
    localStorage.setItem(STORAGE_KEY, theme.value)
  } catch {
    // 浏览器禁止存储时只在本次会话生效。
  }
}
