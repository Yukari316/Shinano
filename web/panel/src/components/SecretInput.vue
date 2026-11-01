<script setup lang="ts">
// 只写的机密输入框：已保存的值不会下发，输入框始终从空开始，留空表示不修改。状态与清除见 SecretStatus。
const props = defineProps<{ configured: boolean; cleared: boolean }>()
const value = defineModel<string>({ default: '' })
</script>

<template>
  <input
    v-model="value"
    type="password"
    autocomplete="new-password"
    class="input w-full max-w-md"
    :disabled="props.cleared"
    :placeholder="props.cleared ? '保存后清除' : props.configured ? '输入新值以替换' : '未配置'"
  />
</template>
