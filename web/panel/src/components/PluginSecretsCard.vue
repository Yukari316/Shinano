<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import type { PluginSecretField } from '../types'
import SecretInput from './SecretInput.vue'
import SecretStatus from './SecretStatus.vue'

// 插件机密只写：宿主只返回各项是否已配置，保存后插件立即读到新值，不需要重启。
const props = defineProps<{ pluginId: string }>()

const fields = ref<PluginSecretField[]>([])
const drafts = ref<Record<string, string>>({})
const clears = ref<Record<string, boolean>>({})
const error = ref('')
const saved = ref(false)

const changes = computed(() => {
  const result: Record<string, string | null> = {}
  for (const field of fields.value) {
    if (clears.value[field.key]) result[field.key] = null
    else if (drafts.value[field.key]) result[field.key] = drafts.value[field.key]!
  }
  return result
})
const pendingCount = computed(() => Object.keys(changes.value).length)

onMounted(load)

async function load(): Promise<void> {
  try {
    fields.value = await api.get<PluginSecretField[]>(`/api/plugins/${props.pluginId}/secrets`)
    reset()
  } catch (e) {
    error.value = describeError(e)
  }
}

function reset(): void {
  drafts.value = Object.fromEntries(fields.value.map((field) => [field.key, '']))
  clears.value = Object.fromEntries(fields.value.map((field) => [field.key, false]))
}

// 提交后无论成败都清空输入，机密不在页面中停留。
async function save(): Promise<void> {
  error.value = ''
  saved.value = false
  const body = changes.value
  reset()
  try {
    await api.put(`/api/plugins/${props.pluginId}/secrets`, body)
    saved.value = true
    await load()
  } catch (e) {
    error.value = describeError(e)
  }
}
</script>

<template>
  <section class="surface card">
    <form class="card-body" @submit.prevent="save">
      <h2 class="card-title">机密</h2>
      <p class="text-sm opacity-70">已保存的值不会显示；输入新值即替换，留空表示不修改。</p>
      <fieldset v-for="field in fields" :key="field.key" class="fieldset">
        <legend class="fieldset-legend">
          {{ field.label }}
          <SecretStatus v-model:cleared="clears[field.key]" :configured="field.configured" :clearable="field.editable" />
        </legend>
        <SecretInput v-if="field.editable" v-model="drafts[field.key]" :configured="field.configured" :cleared="Boolean(clears[field.key])" />
        <p v-if="field.description" class="label whitespace-normal">{{ field.description }}</p>
      </fieldset>
      <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
      <div v-if="saved" role="status" class="alert alert-success">已保存，插件立即使用新值</div>
      <div class="card-actions justify-end">
        <button type="submit" class="btn btn-primary" :disabled="pendingCount === 0">保存</button>
      </div>
    </form>
  </section>
</template>
