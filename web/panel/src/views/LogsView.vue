<script setup lang="ts">
import { PhPause, PhPlay } from '@phosphor-icons/vue'
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import LogList from '../components/LogList.vue'
import PageToolbar from '../components/PageToolbar.vue'
import { formatBytes, formatTime } from '../format'
import { logsPaused, subscribeLogs, togglePause } from '../logStream'
import type { LogFile } from '../types'

const TAIL_LINES = 1000
const LEVEL_PATTERN = /^(\[[^\]]+ )(VRB|DBG|INF|WRN|ERR|FTL)(\].*)$/

// 进入日志页时由本页订阅实时日志，右侧日志面板同时隐藏，本标签页只有一个连接。
const release = subscribeLogs()
onBeforeUnmount(release)

const tab = ref<'live' | 'history'>('live')
const files = ref<LogFile[]>([])
const selectedFile = ref<string | null>(null)
const fileLines = ref<string[]>([])
const fileView = ref<HTMLElement | null>(null)
const error = ref('')

onMounted(async () => {
  try {
    files.value = await api.get<LogFile[]>('/api/logs/files')
  } catch (e) {
    error.value = describeError(e)
  }
})

async function openFile(name: string): Promise<void> {
  selectedFile.value = name
  error.value = ''
  try {
    const result = await api.get<{ lines: string[] }>(`/api/logs/files/${encodeURIComponent(name)}?lines=${TAIL_LINES}`)
    fileLines.value = result.lines
    // 读取的是文件末尾，打开后定位到最新一行。
    await nextTick()
    fileView.value?.scrollTo({ top: fileView.value.scrollHeight })
  } catch (e) {
    error.value = describeError(e)
  }
}

// 历史日志是纯文本，只为等级着色；参数在写入文件时已渲染，无法再区分类型。
function levelOf(line: string): { before: string; level: string; after: string } | null {
  const match = LEVEL_PATTERN.exec(line)
  return match ? { before: match[1] ?? '', level: match[2] ?? '', after: match[3] ?? '' } : null
}

const LEVEL_CLASS: Record<string, string> = {
  INF: 'text-info',
  WRN: 'text-warning-strong font-medium',
  ERR: 'text-error-strong font-medium',
  FTL: 'text-error-strong font-medium',
  DBG: 'opacity-60',
  VRB: 'opacity-60'
}
</script>

<template>
  <PageToolbar>
    <div role="tablist" class="tabs tabs-box w-fit">
      <button role="tab" class="tab" :class="{ 'tab-active': tab === 'live' }" :aria-selected="tab === 'live'" @click="tab = 'live'">实时日志</button>
      <button role="tab" class="tab" :class="{ 'tab-active': tab === 'history' }" :aria-selected="tab === 'history'" @click="tab = 'history'">历史日志</button>
    </div>
    <button v-if="tab === 'live'" class="btn btn-sm btn-ghost surface" @click="togglePause">
      <PhPlay v-if="logsPaused" class="size-4" aria-hidden="true" />
      <PhPause v-else class="size-4" aria-hidden="true" />
      {{ logsPaused ? '继续滚动' : '暂停滚动' }}
    </button>
  </PageToolbar>
  <div v-if="error" role="alert" class="alert alert-error shrink-0">{{ error }}</div>

  <section v-if="tab === 'live'" class="surface card min-h-0 flex-1">
    <LogList layout="row" />
  </section>

  <!-- 历史日志：左侧文件列表、右侧文件内容，各自滚动；窄屏时上下排列。 -->
  <div v-else class="grid min-h-0 flex-1 grid-rows-[minmax(0,2fr)_minmax(0,3fr)] gap-4 lg:grid-cols-[18rem_minmax(0,1fr)] lg:grid-rows-1">
    <section class="surface card min-h-0">
      <h2 class="px-4 pb-2 pt-4 text-sm font-semibold">日志文件 <span class="font-normal opacity-60">{{ files.length }}</span></h2>
      <ul class="min-h-0 flex-1 overflow-y-auto px-2 pb-2">
        <li v-for="file in files" :key="file.name">
          <button
            class="flex w-full flex-col rounded-field px-2 py-1.5 text-left hover:bg-[var(--raise)]"
            :class="{ 'bg-[var(--raise-strong)]': selectedFile === file.name }"
            :aria-current="selectedFile === file.name"
            @click="openFile(file.name)"
          >
            <span class="truncate font-mono text-xs">{{ file.name }}</span>
            <span class="text-xs opacity-60">{{ formatBytes(file.size) }} · {{ formatTime(file.modified) }}</span>
          </button>
        </li>
        <li v-if="files.length === 0" class="px-2 py-4 text-center text-sm opacity-60">暂无日志文件</li>
      </ul>
    </section>
    <section class="surface card min-h-0">
      <h2 class="truncate px-4 pb-2 pt-4 text-sm font-semibold">
        <template v-if="selectedFile">{{ selectedFile }} <span class="font-normal opacity-60">最后 {{ TAIL_LINES }} 行</span></template>
        <template v-else>文件内容</template>
      </h2>
      <div ref="fileView" class="min-h-0 flex-1 overflow-auto px-4 pb-3 font-mono text-xs leading-5">
        <p v-if="!selectedFile" class="py-6 text-center font-sans text-sm opacity-60">从左侧选择一个日志文件</p>
        <div v-for="(line, index) in fileLines" :key="index" class="whitespace-pre-wrap [overflow-wrap:anywhere]">
          <template v-if="levelOf(line)">
            {{ levelOf(line)?.before }}<span :class="LEVEL_CLASS[levelOf(line)?.level ?? '']">{{ levelOf(line)?.level }}</span>{{ levelOf(line)?.after }}
          </template>
          <template v-else>{{ line }}</template>
        </div>
      </div>
    </section>
  </div>
</template>
