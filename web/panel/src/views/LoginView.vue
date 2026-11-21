<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, describeError } from '../api'

const route = useRoute()
const router = useRouter()
const token = ref('')
const error = ref('')
const busy = ref(false)

async function login(): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    await api.post('/api/auth/login', { token: token.value.trim() })
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/') ? route.query.redirect : '/'
    await router.replace(redirect)
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center p-4">
    <form class="card w-full max-w-md bg-base-100 shadow-xl" @submit.prevent="login">
      <div class="card-body">
        <h1 class="card-title text-2xl">Shinano 管理面板</h1>
        <fieldset class="fieldset">
          <legend class="fieldset-legend">面板 Token</legend>
          <input v-model="token" type="password" class="input w-full" autocomplete="current-password" required autofocus />
          <p class="label whitespace-normal">Token 保存在部署数据目录的部署配置文件中。</p>
        </fieldset>
        <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
        <div class="card-actions justify-end">
          <button type="submit" class="btn btn-primary" :disabled="busy">登录</button>
        </div>
      </div>
    </form>
  </div>
</template>
