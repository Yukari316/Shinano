<script setup lang="ts">
import { ref, watch } from 'vue'
import { describeError } from '../api'
import { beginRestart, CHANGE_KIND_LABEL, describeChange, modeLabel, restartDialogOpen, restartStatus } from '../restart'

const dialog = ref<HTMLDialogElement | null>(null)
const error = ref('')
const busy = ref(false)

watch(restartDialogOpen, (open) => {
  error.value = ''
  if (open) dialog.value?.showModal()
  else dialog.value?.close()
})

async function confirm(): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    await beginRestart()
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <dialog ref="dialog" class="modal" @close="restartDialogOpen = false">
    <div class="modal-box">
      <h3 class="text-lg font-bold">确认重启 Shinano？</h3>
      <p class="py-2 text-sm opacity-80">
        重启期间 Bot 暂停响应，面板会在新进程就绪后自动恢复<template v-if="restartStatus">；重启方式：{{ modeLabel(restartStatus.mode) }}</template>。
      </p>
      <template v-if="restartStatus && restartStatus.pending.length > 0">
        <p class="pt-2 text-sm font-semibold">将应用以下插件包变更：</p>
        <ul class="mt-1 flex flex-col gap-1 text-sm">
          <li v-for="item in restartStatus.pending" :key="item.change.pluginId" class="flex gap-2">
            <span class="badge badge-sm" :class="item.change.kind === 'Remove' ? 'badge-warning' : 'badge-info'">{{ CHANGE_KIND_LABEL[item.change.kind] }}</span>
            <span class="font-medium">{{ item.change.name }}</span>
            <span class="opacity-70">{{ describeChange(item.change) }}</span>
          </li>
        </ul>
        <p class="pt-2 text-xs opacity-70">新版本 60 秒内未就绪或插件包加载失败时，自动还原上一套插件包。</p>
      </template>
      <p v-else class="pt-2 text-sm opacity-70">没有待处理的插件包变更，只重新读取配置。</p>
      <div v-if="error" role="alert" class="alert alert-error mt-3">{{ error }}</div>
      <div class="modal-action">
        <button class="btn" :disabled="busy" @click="restartDialogOpen = false">取消</button>
        <button class="btn btn-warning" :disabled="busy" @click="confirm">重启</button>
      </div>
    </div>
  </dialog>
</template>
