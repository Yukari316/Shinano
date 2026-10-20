<script setup lang="ts">
import { formatNumber } from '../format'
import type { SenderStat } from '../types'

defineProps<{ senders: SenderStat[] }>()
</script>

<template>
  <table class="table table-sm">
    <thead>
      <tr>
        <th>发言者</th>
        <th class="text-right">消息</th>
        <th class="text-right">字数</th>
        <th class="text-right">图片</th>
        <th class="text-right">语音</th>
        <th class="text-right">视频</th>
        <th class="text-right">文件</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="sender in senders" :key="sender.senderId">
        <td>
          {{ sender.name ?? sender.senderId }}
          <span v-if="sender.name" class="ml-1 text-xs opacity-60">{{ sender.senderId }}</span>
          <span v-if="sender.isBot" class="badge badge-ghost badge-sm ml-1">Bot</span>
        </td>
        <td class="text-right">{{ formatNumber(sender.messages) }}</td>
        <td class="text-right">{{ formatNumber(sender.chars) }}</td>
        <td class="text-right">{{ formatNumber(sender.images) }}</td>
        <td class="text-right">{{ formatNumber(sender.audios) }}</td>
        <td class="text-right">{{ formatNumber(sender.videos) }}</td>
        <td class="text-right">{{ formatNumber(sender.files) }}</td>
      </tr>
      <tr v-if="senders.length === 0">
        <td colspan="7" class="text-center opacity-60">暂无数据</td>
      </tr>
    </tbody>
  </table>
</template>
