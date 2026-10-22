<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { RESTART_SLOW_MS, restartStartedAt } from '../restart'

const TICK_MS = 1_000
const now = ref(Date.now())
let timer: number | undefined

onMounted(() => {
  timer = window.setInterval(() => (now.value = Date.now()), TICK_MS)
})
onBeforeUnmount(() => window.clearInterval(timer))
</script>

<template>
  <div v-if="restartStartedAt !== null" class="fixed inset-0 z-50 grid place-items-center bg-base-300/80 backdrop-blur-sm" role="alertdialog" aria-live="assertive" aria-label="正在重启">
    <div class="surface card w-96 max-w-[calc(100vw-2rem)]">
      <div class="card-body items-center gap-3 text-center">
        <span class="loading loading-spinner loading-lg" aria-hidden="true"></span>
        <h2 class="card-title">正在重启</h2>
        <p class="text-sm opacity-80">已等待 {{ Math.floor((now - restartStartedAt) / 1000) }} 秒，新进程就绪后自动刷新页面。</p>
        <p v-if="now - restartStartedAt > RESTART_SLOW_MS" class="text-warning-strong text-sm">
          超过 90 秒仍未恢复，请在服务器上查看日志目录中的宿主日志与 restarter 日志；页面会继续等待。
        </p>
      </div>
    </div>
  </div>
</template>
