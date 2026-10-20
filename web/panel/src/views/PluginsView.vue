<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import { plugins, refreshPlugins } from '../plugins'
import type { PluginSnapshot } from '../types'

const error = ref('')
const busy = ref(false)

onMounted(() => run(refreshPlugins))

async function run(action: () => Promise<void>): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    await action()
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

function toggle(plugin: PluginSnapshot): Promise<void> {
  return run(async () => {
    await api.post(`/api/plugins/${plugin.id}/${plugin.enabled ? 'disable' : 'enable'}`)
    await refreshPlugins()
  })
}

// 插件顺序同时决定跨插件指令优先级与普通事件链顺序，从下一条消息生效。
function move(index: number, offset: number): Promise<void> {
  return run(async () => {
    const ids = plugins.value.map((plugin) => plugin.id)
    const [moved] = ids.splice(index, 1)
    if (moved === undefined) return
    ids.splice(index + offset, 0, moved)
    await api.put('/api/plugins/order', ids)
    await refreshPlugins()
  })
}

function commandCount(plugin: PluginSnapshot): number {
  return plugin.commandTypes.reduce((sum, type) => sum + type.commandCount, 0)
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
    <section class="surface card">
      <div class="card-body">
        <p class="text-sm opacity-70">顺序靠前的插件先处理指令与事件；启停与排序从下一条消息生效。</p>
        <table class="table">
          <thead>
            <tr><th class="w-24">顺序</th><th>插件</th><th class="w-48">指令</th><th class="w-24">启用</th></tr>
          </thead>
          <tbody>
            <tr v-for="(plugin, index) in plugins" :key="plugin.id">
              <td>
                <div class="join">
                  <button class="btn join-item btn-xs" :disabled="busy || index === 0" @click="move(index, -1)">↑</button>
                  <button class="btn join-item btn-xs" :disabled="busy || index === plugins.length - 1" @click="move(index, 1)">↓</button>
                </div>
              </td>
              <td>
                <div class="font-semibold">
                  <RouterLink v-if="plugin.hasConfig || plugin.pageTitle" :to="`/plugins/${plugin.id}`" class="link">{{ plugin.name || plugin.id }}</RouterLink>
                  <span v-else>{{ plugin.name || plugin.id }}</span>
                  <span class="ml-2 text-sm opacity-60">{{ plugin.id }} {{ plugin.version }}</span>
                </div>
                <div class="text-sm opacity-70">{{ plugin.description }}</div>
                <div v-if="plugin.error" class="text-sm text-error">{{ plugin.error }}</div>
              </td>
              <td>
                <span v-if="plugin.commandTypes.length === 0" class="text-sm opacity-60">首次启用后显示</span>
                <div v-else class="text-sm">
                  共 {{ commandCount(plugin) }} 条
                  <div v-for="type in plugin.commandTypes" :key="type.name" class="opacity-70">
                    {{ type.name }}：{{ type.commandCount }}<span v-if="!type.enabled">（未生效）</span>
                  </div>
                </div>
              </td>
              <td>
                <input type="checkbox" class="toggle toggle-success" :checked="plugin.enabled" :disabled="busy || plugin.error !== null" @change="toggle(plugin)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
