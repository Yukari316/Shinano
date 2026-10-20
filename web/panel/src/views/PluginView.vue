<script setup lang="ts">
import { createPluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, ref, shallowRef, watch, type Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, describeError, redirectToLogin } from '../api'
import SchemaForm from '../components/SchemaForm.vue'
import { pageTitle } from '../pageTitle'
import { plugins, refreshPlugins } from '../plugins'
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

onMounted(async () => {
  await refreshPlugins().catch(() => undefined)
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
  <div class="flex flex-col gap-4">
    <template v-if="plugin">
      <section v-if="plugin.hasConfig" class="surface card">
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
      <section v-if="plugin.pageTitle" class="surface card">
        <div class="card-body">
          <h2 class="card-title">{{ plugin.pageTitle }}</h2>
          <div v-if="pageError" role="alert" class="alert alert-error">{{ pageError }}</div>
          <component :is="page" v-if="page" :context="context" />
        </div>
      </section>
      <div v-if="!plugin.hasConfig && !plugin.pageTitle" class="alert">该插件没有配置表单；专有页面在插件启用后显示。</div>
    </template>
  </div>
</template>
