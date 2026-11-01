<script setup lang="ts">
// 显示在机密配置名右侧：是否已配置，以及可选项的清除（需确认，保存后生效）。
const props = defineProps<{ configured: boolean; clearable: boolean }>()
const cleared = defineModel<boolean>('cleared', { default: false })

function toggleClear(): void {
  if (!cleared.value && !window.confirm('保存后将清除已保存的值，确认清除？')) return
  cleared.value = !cleared.value
}
</script>

<template>
  <span class="badge badge-sm" :class="cleared ? 'badge-error' : props.configured ? 'badge-success' : 'badge-warning'">
    {{ cleared ? '待清除' : props.configured ? '已配置' : '未配置' }}
  </span>
  <button v-if="props.clearable && props.configured" type="button" class="btn btn-ghost btn-xs" @click="toggleClear">
    {{ cleared ? '撤销' : '清除' }}
  </button>
</template>
