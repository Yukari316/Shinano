<script setup lang="ts">
import { createPluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, ref, shallowRef, watch, type Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, describeError, redirectToLogin } from '../api'
import PluginSecretsCard from '../components/PluginSecretsCard.vue'
import SchemaForm from '../components/SchemaForm.vue'
import { pageTitle } from '../pageTitle'
import { plugins, refreshPlugins } from '../plugins'
import { CHANGE_KIND_LABEL, describeChange, refreshRestart, restartStatus } from '../restart'
import type { PluginConfigForm } from '../types'

const route = useRoute()
const router = useRouter()
const pluginId = String(route.params.id)
const plugin = computed(() => plugins.value.find((item) => item.id === pluginId))

const form = ref<PluginConfigForm | null>(null)
const value = ref<Record<string, unknown>>({})
const formError = ref('')
const saved = ref(false)
const page = shallowRef<Component | null>(null)
const pageError = ref('')
const context = createPluginPageContext(pluginId, redirectToLogin)
const pending = computed(() => restartStatus.value?.pending.find((item) => item.change.pluginId === pluginId) ?? null)
const commandCount = computed(() => plugin.value?.commandTypes.reduce((sum, type) => sum + type.commandCount, 0) ?? 0)

onMounted(async () => {
  await Promise.all([refreshPlugins(), refreshRestart()]).catch(() => undefined)
  if (!plugin.value) {
    await router.replace('/plugins')
    return
  }
  pageTitle.value = plugin.value.name || plugin.value.id
  if (plugin.value.hasConfig) await loadForm()
})

// 专有页面只在插件启用时存在；停用后移除页面组件。
watch(
  () => plugin.value?.pageEntry ?? null,
  async (entry) => {
    page.value = null
    pageError.value = ''
    if (entry === null) return
    try {
      const module = (await import(/* @vite-ignore */ `/plugin-web/${pluginId}/${entry}`)) as { default: Component }
      page.value = module.default
    } catch (e) {
      pageError.value = `插件页面加载失败：${describeError(e)}`
    }
  },
  { immediate: true }
)

async function loadForm(): Promise<void> {
  try {
    form.value = await api.get<PluginConfigForm>(`/api/plugins/${pluginId}/config`)
    value.value = { ...form.value.value }
  } catch (e) {
    formError.value = describeError(e)
  }
}

async function save(): Promise<void> {
  formError.value = ''
  saved.value = false
  try {
    await api.put(`/api/plugins/${pluginId}/config`, value.value)
    saved.value = true
  } catch (e) {
    formError.value = describeError(e)
  }
}
</script>

<template>
  <template v-if="plugin">
    <section class="surface card">
      <div class="card-body gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="card-title">插件信息</h2>
          <span class="badge" :class="plugin.error ? 'badge-error' : plugin.enabled ? 'badge-success' : 'badge-ghost'">
            {{ plugin.error ? '加载失败' : plugin.enabled ? '已启用' : '已停用' }}
          </span>
          <span v-if="pending" class="badge badge-warning">
            待{{ CHANGE_KIND_LABEL[pending.change.kind] }} {{ describeChange(pending.change) }}
          </span>
          <RouterLink to="/plugins" class="link ml-auto text-sm">插件管理</RouterLink>
        </div>
        <p v-if="plugin.description" class="text-sm opacity-80">{{ plugin.description }}</p>
        <div class="grid gap-x-8 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
          <span class="opacity-60">ID</span><span class="font-mono">{{ plugin.id }}</span>
          <span class="opacity-60">版本</span><span class="tabular-nums">{{ plugin.version }}</span>
          <span class="opacity-60">指令</span>
          <span v-if="plugin.commandTypes.length === 0" class="opacity-70">首次启用后显示</span>
          <span v-else>
            共 {{ commandCount }} 条：<template v-for="(type, index) in plugin.commandTypes" :key="type.name">{{ index > 0 ? '、' : '' }}{{ type.name }} {{ type.commandCount }}<template v-if="!type.enabled">（未生效）</template></template>
          </span>
        </div>
        <p v-if="plugin.error" class="text-error-strong text-sm">{{ plugin.error }}</p>
      </div>
    </section>
    <section v-if="plugin.hasConfig" class="surface card" :class="{ 'flex-1': !plugin.pageTitle }">
      <form class="card-body" @submit.prevent="save">
        <h2 class="card-title">配置</h2>
        <SchemaForm v-if="form" v-model="value" :schema="form.schema" />
        <div v-if="formError" role="alert" class="alert alert-error">{{ formError }}</div>
        <div v-if="saved" role="status" class="alert alert-success">已保存，插件下次读取配置时生效</div>
        <div class="card-actions justify-end">
          <button type="submit" class="btn btn-primary">保存</button>
        </div>
      </form>
    </section>
    <section v-else class="surface card" :class="{ 'flex-1': !plugin.pageTitle }">
      <div class="card-body">
        <h2 class="card-title">配置</h2>
        <p class="text-sm opacity-70">无可配置项</p>
      </div>
    </section>
    <PluginSecretsCard v-if="plugin.hasSecrets" :plugin-id="plugin.id" />
    <section v-if="plugin.pageTitle" class="surface card flex-1">
      <div class="card-body">
        <h2 class="card-title">{{ plugin.pageTitle }}</h2>
        <div v-if="pageError" role="alert" class="alert alert-error">{{ pageError }}</div>
        <component :is="page" v-if="page" :context="context" />
      </div>
    </section>
  </template>
</template>
