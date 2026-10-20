<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, describeError } from '../api'
import SenderTable from '../components/SenderTable.vue'
import PageToolbar from '../components/PageToolbar.vue'
import StatFilters from '../components/StatFilters.vue'
import { formatNumber } from '../format'
import type { ConversationStat, SenderStat } from '../types'

const route = useRoute()
const scope = route.meta.scope ?? 'Group'

const days = ref(30)
const includeBot = ref(false)
const conversations = ref<ConversationStat[]>([])
const selected = ref<number | null>(null)
const senders = ref<SenderStat[]>([])
const error = ref('')

function query(): string {
  return `days=${days.value}&includeBot=${includeBot.value}&scope=${scope}`
}

watch(
  [days, includeBot],
  async () => {
    error.value = ''
    try {
      conversations.value = await api.get<ConversationStat[]>(`/api/stats/conversations?${query()}`)
      if (selected.value !== null) await loadSenders(selected.value)
    } catch (e) {
      error.value = describeError(e)
    }
  },
  { immediate: true }
)

async function loadSenders(conversationId: number): Promise<void> {
  selected.value = conversationId
  try {
    senders.value = await api.get<SenderStat[]>(`/api/stats/senders?${query()}&conversationId=${conversationId}&limit=100`)
  } catch (e) {
    error.value = describeError(e)
  }
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <PageToolbar>
      <StatFilters v-model:days="days" v-model:include-bot="includeBot" />
    </PageToolbar>
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
    <section class="surface card">
      <div class="card-body">
        <h2 class="card-title">会话</h2>
        <table class="table table-sm">
          <thead>
            <tr>
              <th>会话</th>
              <th class="text-right">发言人数</th>
              <th class="text-right">消息</th>
              <th class="text-right">字数</th>
              <th class="text-right">图片</th>
              <th class="text-right">文件</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="conversation in conversations"
              :key="`${conversation.type}-${conversation.conversationId}`"
              class="cursor-pointer hover:bg-[var(--raise)]"
              :class="{ 'bg-[var(--raise-strong)]': selected === conversation.conversationId }"
              @click="loadSenders(conversation.conversationId)"
            >
              <td>
                {{ conversation.name ?? conversation.conversationId }}
                <span v-if="conversation.name" class="ml-1 text-xs opacity-60">{{ conversation.conversationId }}</span>
                <span v-if="conversation.type === 'Temp'" class="badge badge-ghost badge-sm ml-1">临时会话</span>
              </td>
              <td class="text-right">{{ conversation.senders }}</td>
              <td class="text-right">{{ formatNumber(conversation.messages) }}</td>
              <td class="text-right">{{ formatNumber(conversation.chars) }}</td>
              <td class="text-right">{{ formatNumber(conversation.images) }}</td>
              <td class="text-right">{{ formatNumber(conversation.files) }}</td>
            </tr>
            <tr v-if="conversations.length === 0">
              <td colspan="6" class="text-center opacity-60">暂无数据</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <section v-if="selected !== null" class="surface card">
      <div class="card-body">
        <h2 class="card-title">发言者</h2>
        <SenderTable :senders="senders" />
      </div>
    </section>
  </div>
</template>
