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
  <section v-if="status" class="surface card flex-1">
    <div class="card-body">
      <h2 class="card-title">群列表 <span v-if="status.groups" class="text-sm font-normal opacity-60">{{ status.groups.length }} 个</span></h2>
      <p v-if="!status.connected" class="py-6 text-center text-sm opacity-60">Bot 未连接，连接后显示群列表</p>
      <table v-else-if="status.groups" class="table">
        <thead>
          <tr><th>群名</th><th class="w-40">群号</th><th class="w-40 text-right">成员</th></tr>
        </thead>
        <tbody>
          <tr v-for="group in status.groups" :key="group.groupId">
            <td>{{ group.name || '—' }}</td>
            <td class="tabular-nums opacity-70">{{ group.groupId }}</td>
            <td class="text-right tabular-nums">{{ group.memberCount }} / {{ group.maxMemberCount }}</td>
          </tr>
          <tr v-if="status.groups.length === 0">
            <td colspan="3" class="text-center opacity-60">Bot 尚未加入任何群</td>
          </tr>
        </tbody>
      </table>
      <p v-else class="py-6 text-center text-sm opacity-60">LLBot 未返回群列表</p>
    </div>
  </section>
</template>
