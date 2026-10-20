<script setup lang="ts">
const days = defineModel<number>('days', { required: true })
const includeBot = defineModel<boolean>('includeBot', { required: true })

const RANGES = [7, 30, 90, 365]
</script>

<template>
  <div class="surface flex w-full flex-wrap items-center gap-4 rounded-box px-4 py-3">
    <div class="join">
      <button
        v-for="range in RANGES"
        :key="range"
        class="btn join-item btn-sm"
        :class="{ 'btn-active': days === range }"
        @click="days = range"
      >
        近 {{ range }} 天
      </button>
    </div>
    <label class="flex cursor-pointer items-center gap-2 text-sm">
      <input v-model="includeBot" type="checkbox" class="toggle toggle-sm" />
      包含 Bot 发送的消息
    </label>
  </div>
</template>
