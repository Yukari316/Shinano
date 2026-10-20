<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import { formatTime } from '../format'
import type { ArchiveGap } from '../types'

const CAUSE_TEXT: Record<ArchiveGap['cause'], string> = {
  DatabaseOffline: '数据库离线',
  BotDisconnected: 'LLBot 断线',
  Backlog: '归档积压'
}

const gaps = ref<ArchiveGap[]>([])
const error = ref('')

onMounted(async () => {
  try {
    gaps.value = await api.get<ArchiveGap[]>('/api/archive-gaps')
  } catch (e) {
    error.value = describeError(e)
  }
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
    <section class="surface card">
      <div class="card-body">
        <h2 class="card-title">缺口记录</h2>
        <p class="text-sm opacity-70">以下时间段内的消息未能完整归档，且无法补回。</p>
        <table class="table">
          <thead>
            <tr>
              <th>原因</th>
              <th>开始</th>
              <th>结束</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="gap in gaps" :key="`${gap.cause}-${gap.startedAt}`">
              <td><span class="badge badge-warning">{{ CAUSE_TEXT[gap.cause] }}</span></td>
              <td>{{ formatTime(gap.startedAt) }}</td>
              <td>{{ formatTime(gap.endedAt) }}</td>
            </tr>
            <tr v-if="gaps.length === 0">
              <td colspan="3" class="text-center opacity-60">没有归档缺口</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
