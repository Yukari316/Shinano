<script setup lang="ts">
import { PhCaretUp, PhPlus, PhTrash } from '@phosphor-icons/vue'
import { callApi, formatTime, jsonBody, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, ref, watch } from 'vue'
import AnswerPreview from './AnswerPreview.vue'
import EntryForm from './EntryForm.vue'
import type { Entry, ForbiddenQuestion, Group, MatchType } from './types'

const props = defineProps<{ context: PluginPageContext }>()

const tab = ref<'entries' | 'forbidden'>('entries')
const groups = ref<Group[]>([])
const entries = ref<Entry[]>([])
const forbidden = ref<ForbiddenQuestion[]>([])
const filterGroup = ref<number | ''>('')
const filterType = ref<MatchType | ''>('')
const keyword = ref('')
const showForm = ref(false)
const newForbidden = ref('')
const error = ref('')

const groupNames = computed(() => new Map(groups.value.map((group) => [group.groupId, group.name])))

async function call<T>(path: string, init?: RequestInit): Promise<T | null> {
  const result = await callApi<T>(props.context, path, init)
  error.value = result.error ?? ''
  return result.ok ? result.body : null
}

async function loadEntries() {
  const query = new URLSearchParams()
  if (filterGroup.value !== '') query.set('groupId', String(filterGroup.value))
  if (filterType.value !== '') query.set('matchType', filterType.value)
  if (keyword.value.trim()) query.set('keyword', keyword.value.trim())
  entries.value = (await call<Entry[]>(`/entries?${query}`)) ?? []
}

async function loadForbidden() {
  forbidden.value = (await call<ForbiddenQuestion[]>('/forbidden')) ?? []
}

async function removeEntry(entry: Entry) {
  if (!window.confirm(`删除问题「${entry.question}」？`)) return
  await call(`/entries/${entry.id}`, { method: 'DELETE' })
  await loadEntries()
}

async function addForbidden() {
  if (!newForbidden.value.trim()) return
  await call('/forbidden', jsonBody('POST', { question: newForbidden.value.trim() }))
  if (!error.value) newForbidden.value = ''
  await loadForbidden()
}

async function removeForbidden(item: ForbiddenQuestion) {
  await call(`/forbidden/${item.id}`, { method: 'DELETE' })
  await loadForbidden()
}

function onSaved() {
  showForm.value = false
  void loadEntries()
}

let keywordTimer: number | undefined
watch([filterGroup, filterType], () => void loadEntries())
watch(keyword, () => {
  window.clearTimeout(keywordTimer)
  keywordTimer = window.setTimeout(() => void loadEntries(), 300)
})

onMounted(async () => {
  groups.value = (await call<Group[]>('/groups')) ?? []
  await Promise.all([loadEntries(), loadForbidden()])
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div role="tablist" class="tabs tabs-border">
      <button role="tab" class="tab" :class="{ 'tab-active': tab === 'entries' }" @click="tab = 'entries'">词条</button>
      <button role="tab" class="tab" :class="{ 'tab-active': tab === 'forbidden' }" @click="tab = 'forbidden'">禁止提问词</button>
    </div>

    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

    <template v-if="tab === 'entries'">
      <div class="flex flex-wrap items-end gap-3">
        <select v-model="filterGroup" class="select select-sm w-56" aria-label="按群筛选">
          <option value="">全部群</option>
          <option v-for="group in groups" :key="group.groupId" :value="group.groupId">{{ group.name }}（{{ group.groupId }}）</option>
        </select>
        <select v-model="filterType" class="select select-sm w-32" aria-label="按匹配方式筛选">
          <option value="">全部方式</option>
          <option value="Full">全字</option>
          <option value="Regex">正则</option>
        </select>
        <input v-model="keyword" class="input input-sm w-56" placeholder="问题关键字" aria-label="问题关键字" />
        <button class="btn btn-primary btn-sm ml-auto" @click="showForm = !showForm"><component :is="showForm ? PhCaretUp : PhPlus" class="size-4" />{{ showForm ? '收起' : '新增词条' }}</button>
      </div>

      <EntryForm v-if="showForm" :context="context" :groups="groups" @saved="onSaved" />

      <p class="text-sm opacity-70">共 {{ entries.length }} 条，越新录入的越优先；同一条消息命中多条时只回答最新的一条。</p>
      <ul class="flex flex-col gap-2">
        <li v-for="entry in entries" :key="entry.id" class="tile flex flex-col gap-2 p-3">
          <div class="flex flex-wrap items-center gap-2">
            <span v-if="entry.matchType === 'Regex'" class="badge badge-sm badge-info">RG</span>
            <span class="font-semibold [overflow-wrap:anywhere]">{{ entry.question }}</span>
            <span class="text-xs opacity-60">{{ groupNames.get(entry.groupId) ?? '' }}（{{ entry.groupId }}）· {{ formatTime(entry.createdAt) }}{{ entry.createdBy ? ` · ${entry.createdBy}` : ' · 面板' }}</span>
            <button class="btn btn-ghost btn-xs ml-auto text-error-strong" :aria-label="`删除 ${entry.question}`" @click="removeEntry(entry)"><PhTrash class="size-4" />删除</button>
          </div>
          <AnswerPreview :answer="entry.answer" :plugin-id="context.pluginId" />
        </li>
        <li v-if="entries.length === 0" class="py-6 text-center text-sm opacity-60">没有词条</li>
      </ul>
    </template>

    <template v-else>
      <p class="text-sm opacity-70">录入时问题文字（全字或正则表达式）与某一条完全相同即拒绝；对所有机器人账号与群生效。</p>
      <form class="flex gap-2" @submit.prevent="addForbidden">
        <input v-model="newForbidden" class="input input-sm w-72" maxlength="200" placeholder="不能作为问题的文字" aria-label="新的禁止提问词" />
        <button class="btn btn-primary btn-sm" :disabled="!newForbidden.trim()"><PhPlus class="size-4" />添加</button>
      </form>
      <ul class="flex flex-col gap-1">
        <li v-for="item in forbidden" :key="item.id" class="tile flex items-center gap-2 px-3 py-2">
          <span class="[overflow-wrap:anywhere]">{{ item.question }}</span>
          <button class="btn btn-ghost btn-xs ml-auto text-error-strong" :aria-label="`删除 ${item.question}`" @click="removeForbidden(item)"><PhTrash class="size-4" />删除</button>
        </li>
        <li v-if="forbidden.length === 0" class="py-6 text-center text-sm opacity-60">没有禁止提问词</li>
      </ul>
    </template>
  </div>
</template>
