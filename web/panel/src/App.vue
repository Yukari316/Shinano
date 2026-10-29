<script setup lang="ts">
import { PhMoon, PhSun } from '@phosphor-icons/vue'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { appearance, backgroundUrl, refreshAppearance } from './appearance'
import LogPanel from './components/LogPanel.vue'
import RestartDialog from './components/RestartDialog.vue'
import RestartOverlay from './components/RestartOverlay.vue'
import SideNav from './components/SideNav.vue'
import { refreshPlugins } from './plugins'
import { pageSubtitle, pageTitle } from './pageTitle'
import { refreshRestart } from './restart'
import { startOverviewPolling, stopOverviewPolling } from './status'
import { DARK_THEME, theme, toggleTheme } from '@shinano/theme'

const route = useRoute()
const isPublic = computed(() => route.meta.public === true)
// 日志页自己展示实时日志，此时不显示右侧日志面板，整个面板只保持一个连接。
const showLogPanel = computed(() => !isPublic.value && route.name !== 'logs')

const title = computed(() => pageTitle.value ?? route.meta.title)
const fill = computed(() => route.meta.fill === true)
watch(
  () => route.fullPath,
  () => {
    pageTitle.value = null
    pageSubtitle.value = null
  }
)

// 内容区上下边缘外还有内容时，对应边缘淡出，避免卡片在固定的标题、工具栏或底部操作栏旁被硬切。
const scroller = ref<HTMLElement | null>(null)
const fadeTop = ref(false)
const fadeBottom = ref(false)

function updateEdges() {
  const element = scroller.value
  if (!element) return
  fadeTop.value = element.scrollTop > 0
  fadeBottom.value = Math.ceil(element.scrollTop + element.clientHeight) < element.scrollHeight
  element.style.setProperty('--scrollbar-width', `${element.offsetWidth - element.clientWidth}px`)
}

// 页面内容异步加载或展开折叠时高度会变，窗口缩放时可视高度会变，两者都要重新判断。
watch(scroller, (element, _, onCleanup) => {
  if (!element) return
  const observer = new ResizeObserver(updateEdges)
  observer.observe(element)
  if (element.firstElementChild) observer.observe(element.firstElementChild)
  onCleanup(() => observer.disconnect())
})

const hasBackground = computed(() => backgroundUrl.value !== null)
const shellClass = computed(() => ({ 'has-bg': hasBackground.value, frosted: hasBackground.value && appearance.value.blur }))
const shellStyle = computed(() => ({
  '--panel-alpha': String(appearance.value.panelOpacity),
  '--card-alpha': String(appearance.value.cardOpacity),
  '--veil-alpha': String(appearance.value.veilOpacity),
  '--bg-image': backgroundUrl.value ? `url(${backgroundUrl.value})` : undefined
}))

// 登录后加载侧边栏、外观与总览状态；加载失败时保持默认值，不影响页面。
watch(
  isPublic,
  (publicPage) => {
    if (publicPage) {
      stopOverviewPolling()
      return
    }
    refreshPlugins().catch(() => undefined)
    refreshAppearance().catch(() => undefined)
    refreshRestart().catch(() => undefined)
    startOverviewPolling()
  },
  { immediate: true }
)
</script>

<template>
  <div v-if="isPublic" class="min-h-dvh bg-base-200">
    <RouterView />
  </div>
  <div v-else class="app-shell flex h-dvh gap-3 p-3" :class="shellClass" :style="shellStyle">
    <aside class="surface w-60 shrink-0 overflow-y-auto rounded-box">
      <SideNav />
    </aside>
    <!-- 标题、工具栏与底部操作栏固定，只有中间的页面内容滚动。 -->
    <main class="veil flex min-w-0 flex-1 flex-col overflow-hidden rounded-box">
      <header class="flex flex-wrap items-center justify-between gap-3 px-6 pb-4 pt-5">
        <div class="flex min-w-0 flex-col gap-0.5">
          <h1 class="text-2xl font-bold [text-wrap:balance]">{{ title }}</h1>
          <p v-if="pageSubtitle" class="text-sm opacity-70">{{ pageSubtitle }}</p>
        </div>
        <button class="btn btn-sm btn-ghost border-[var(--line)]" :aria-label="theme === DARK_THEME ? '切换到亮色主题' : '切换到暗色主题'" @click="toggleTheme">
          <PhSun v-if="theme === DARK_THEME" class="size-4" aria-hidden="true" />
          <PhMoon v-else class="size-4" aria-hidden="true" />
          {{ theme === DARK_THEME ? '亮色' : '暗色' }}
        </button>
      </header>
      <div id="page-toolbar" class="px-6"></div>
      <div
        ref="scroller"
        class="min-h-0 flex-1 px-6 pb-6"
        :class="fill ? 'flex flex-col' : ['scroll-fade overflow-y-auto', { 'fade-top': fadeTop, 'fade-bottom': fadeBottom }]"
        @scroll.passive="updateEdges"
      >
        <div class="flex flex-col gap-4" :class="fill ? 'min-h-0 flex-1' : 'min-h-full'">
          <RouterView :key="route.fullPath" />
        </div>
      </div>
      <div id="page-footer" class="px-6"></div>
    </main>
    <LogPanel v-if="showLogPanel" />
    <RestartDialog />
    <RestartOverlay />
  </div>
</template>
