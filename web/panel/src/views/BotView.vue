<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import type { BotStatus } from '../types'

const status = ref<BotStatus | null>(null)
const error = ref('')

onMounted(async () => {
  try {
    status.value = await api.get<BotStatus>('/api/bot')
  } catch (e) {
    error.value = describeError(e)
  }
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
    <section v-if="status" class="surface card">
      <div class="card-body">
        <h2 class="card-title">
          Bot 状态
          <span class="badge" :class="status.connected ? 'badge-success' : 'badge-error'">{{ status.connected ? '已连接' : '未连接' }}</span>
        </h2>
        <table class="table">
          <tbody>
            <tr><td class="w-40">账号</td><td>{{ status.selfId || '—' }}</td></tr>
            <tr v-if="status.connected"><td>昵称</td><td>{{ status.nickname ?? '—' }}</td></tr>
            <tr v-if="status.connected"><td>协议端</td><td>{{ status.impl ?? '—' }}</td></tr>
            <tr v-if="status.connected"><td>群数量</td><td>{{ status.groupCount ?? '—' }}</td></tr>
            <tr v-if="status.connected"><td>好友数量</td><td>{{ status.friendCount ?? '—' }}</td></tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
