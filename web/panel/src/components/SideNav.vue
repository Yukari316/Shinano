<script setup lang="ts">
import {
  PhChartBar,
  PhChatsCircle,
  PhCube,
  PhHouse,
  PhPalette,
  PhPuzzlePiece,
  PhRobot,
  PhSignOut,
  PhSlidersHorizontal,
  PhTerminalWindow,
  PhUser,
  PhWarning
} from '@phosphor-icons/vue'
import { computed, type Component } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'
import { plugins } from '../plugins'
import { pendingCount } from '../restart'
import { overview } from '../status'

interface NavItem {
  to: string
  label: string
  icon: Component
  exact?: boolean
}

const router = useRouter()

const main: NavItem[] = [
  { to: '/', label: '总览', icon: PhHouse, exact: true },
  { to: '/bot', label: 'Bot 状态', icon: PhRobot },
  { to: '/plugins', label: '插件管理', icon: PhPuzzlePiece, exact: true }
]
const data: NavItem[] = [
  { to: '/stats', label: '数据统计', icon: PhChartBar },
  { to: '/chats/group', label: '群聊', icon: PhChatsCircle },
  { to: '/chats/private', label: '私聊', icon: PhUser },
  { to: '/gaps', label: '归档缺口', icon: PhWarning },
  { to: '/logs', label: '日志', icon: PhTerminalWindow }
]
const settings: NavItem[] = [
  { to: '/config', label: '宿主配置', icon: PhSlidersHorizontal },
  { to: '/appearance', label: '外观', icon: PhPalette }
]

// 有配置表单或启用中专有页面的插件各占一个选项卡。
const pluginTabs = computed(() =>
  plugins.value
    .filter((plugin) => plugin.hasConfig || plugin.pageTitle !== null)
    .map((plugin) => ({ to: `/plugins/${plugin.id}`, label: plugin.name || plugin.id, icon: PhCube }))
)

const botOnline = computed(() => overview.value?.bot.connected ?? false)

async function logout(): Promise<void> {
  await api.post('/api/auth/logout').catch(() => undefined)
  await router.replace({ name: 'login' })
}
</script>

<template>
  <nav class="flex h-full flex-col gap-1 p-3" aria-label="主导航">
    <div class="flex items-center gap-3 px-2 pb-3 pt-1">
      <div class="grid size-10 place-items-center rounded-field bg-primary text-lg font-bold text-primary-content" aria-hidden="true">S</div>
      <div class="flex min-w-0 flex-col">
        <span class="font-bold">Shinano</span>
        <span class="flex items-center gap-1.5 text-xs opacity-70">
          <span class="size-2 rounded-full" :class="botOnline ? 'bg-success' : 'bg-error'" aria-hidden="true"></span>
          {{ overview?.bot.selfId || '—' }} · {{ botOnline ? '在线' : '离线' }}
        </span>
      </div>
    </div>

    <ul class="menu w-full gap-0.5 p-0">
      <li v-for="item in main" :key="item.to">
        <RouterLink :to="item.to" :exact-active-class="item.exact ? 'menu-active' : undefined" :active-class="item.exact ? undefined : 'menu-active'">
          <component :is="item.icon" class="size-[18px]" aria-hidden="true" />{{ item.label }}
          <span v-if="item.to === '/plugins' && pendingCount > 0" class="badge badge-warning badge-sm ml-auto" :title="`${pendingCount} 项插件包变更待重启生效`">{{ pendingCount }}</span>
        </RouterLink>
      </li>
      <li class="menu-title pt-3">数据</li>
      <li v-for="item in data" :key="item.to">
        <RouterLink :to="item.to" active-class="menu-active"><component :is="item.icon" class="size-[18px]" aria-hidden="true" />{{ item.label }}</RouterLink>
      </li>
      <li class="menu-title pt-3">设置</li>
      <li v-for="item in settings" :key="item.to">
        <RouterLink :to="item.to" active-class="menu-active"><component :is="item.icon" class="size-[18px]" aria-hidden="true" />{{ item.label }}</RouterLink>
      </li>
      <template v-if="pluginTabs.length > 0">
        <li class="menu-title pt-3">插件</li>
        <li v-for="item in pluginTabs" :key="item.to">
          <RouterLink :to="item.to" active-class="menu-active"><component :is="item.icon" class="size-[18px]" aria-hidden="true" />{{ item.label }}</RouterLink>
        </li>
      </template>
    </ul>

    <div class="grow"></div>
    <button class="btn btn-ghost justify-start border-[var(--line)]" @click="logout">
      <PhSignOut class="size-[18px]" aria-hidden="true" />退出登录
    </button>
  </nav>
</template>
