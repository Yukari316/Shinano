<script setup lang="ts">
import { PhArrowsClockwise, PhQrCode, PhX } from '@phosphor-icons/vue'
import { type ApiResult, callApi, formatTime, jsonBody, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { Account, Follow, Group, LoginState, QrState, QrStateView, QrView, Status } from './types'

const props = defineProps<{ context: PluginPageContext }>()

const POLL_MS = 2000
const QR_LIFETIME_MS = 180_000
const STATE_LABEL: Record<LoginState, string> = {
  Unknown: '检查中',
  LoggedOut: '未登录',
  LoggedIn: '已登录',
  Expired: '登录已失效'
}
const STATE_CLASS: Record<LoginState, string> = {
  Unknown: 'badge-ghost',
  LoggedOut: 'badge-ghost',
  LoggedIn: 'badge-success',
  Expired: 'badge-error'
}
const QR_LABEL: Record<QrState, string> = {
  Waiting: '请用 B站手机客户端扫码',
  Scanned: '已扫码，请在手机上确认',
  Succeeded: '登录成功',
  Expired: '二维码已过期',
  Failed: '登录失败，详见日志'
}

const error = ref('')
const status = ref<Status | null>(null)
const qr = ref<QrView | null>(null)
const qrState = ref<QrState | null>(null)
const loginOpen = ref(false)
const replacing = ref<Account | null>(null)
const busy = ref(false)
const follows = ref<Follow[]>([])
const groups = ref<Group[]>([])
const refreshing = ref(false)
const editing = ref<Follow | null>(null)
const selected = ref<number[]>([])
const groupFilter = ref('')
let pollTimer: number | undefined
let qrDeadline = 0
let polling = false

const groupNames = computed(() => new Map(groups.value.map((group) => [group.id, group.name])))
const filteredGroups = computed(() => {
  const keyword = groupFilter.value.trim()
  return keyword ? groups.value.filter((group) => String(group.id).includes(keyword) || (group.name ?? '').includes(keyword)) : groups.value
})

// 409 是需要页面自行处理的业务冲突（如已登录需确认），不作为错误显示。
async function call<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  const result = await callApi<T>(props.context, path, init)
  error.value = result.status === 409 ? '' : (result.error ?? '')
  return result
}

async function loadStatus() {
  status.value = (await call<Status>('/status')).body
}

async function loadFollows() {
  follows.value = (await call<Follow[]>('/follows')).body ?? []
}

async function loadGroups() {
  groups.value = (await call<Group[]>('/groups')).body ?? []
}

async function refreshFollows() {
  refreshing.value = true
  const result = await call<Follow[] | { error: string }>('/follows/refresh', { method: 'POST' })
  refreshing.value = false
  if (result.ok) follows.value = result.body as Follow[]
  else if (result.status === 409) error.value = (result.body as { error: string }).error
}

// 已登录时先在对话框中确认退出原账号（服务端失效并清空浏览器登录数据），确认后再生成二维码。
async function startLogin(confirmLogout = false) {
  stopPolling()
  qr.value = null
  qrState.value = null
  busy.value = true
  const result = await call<QrView | Account>('/login', jsonBody('POST', { confirmLogout }))
  busy.value = false
  if (confirmLogout) await Promise.all([loadStatus(), loadFollows()])
  if (result.status === 409) {
    replacing.value = result.body as Account
    loginOpen.value = true
    return
  }

  replacing.value = null
  if (!result.ok) {
    loginOpen.value = false
    return
  }

  qr.value = result.body as QrView
  qrState.value = 'Waiting'
  loginOpen.value = true
  qrDeadline = Date.now() + QR_LIFETIME_MS
  polling = true
  schedulePoll()
}

// 上一次查询返回后才发下一次：扫码确认后服务端要写入浏览器（十几秒），重叠的查询会把已用掉的二维码误报为过期。
function schedulePoll() {
  if (polling) pollTimer = window.setTimeout(pollLogin, POLL_MS)
}

async function pollLogin() {
  if (!qr.value || !polling) return
  if (Date.now() > qrDeadline) {
    finish('Expired')
    return
  }

  const result = await call<QrStateView>(`/login/${encodeURIComponent(qr.value.key)}`)
  if (!polling) return
  // 请求未送达时继续轮询，二维码过期前恢复连接仍可完成登录。
  if (result.status === 0) {
    schedulePoll()
    return
  }

  const state = result.body?.state ?? 'Failed'
  if (state === 'Waiting' || state === 'Scanned') {
    qrState.value = state
    schedulePoll()
    return
  }

  finish(state)
  if (state === 'Succeeded') {
    await loadStatus()
    await refreshFollows()
  }
}

// 结束后二维码不再显示，只保留结果。
function finish(state: QrState) {
  stopPolling()
  qrState.value = state
  qr.value = null
}

function stopPolling() {
  polling = false
  window.clearTimeout(pollTimer)
  pollTimer = undefined
}

function closeLogin() {
  stopPolling()
  loginOpen.value = false
  replacing.value = null
  qr.value = null
  qrState.value = null
}

async function save(follow: Follow) {
  const result = await call(`/follows/${follow.uid}`, jsonBody('PUT', { pushDynamic: follow.pushDynamic, pushLive: follow.pushLive, groups: follow.groups }))
  if (!result.ok) await loadFollows()
}

function openGroups(follow: Follow) {
  editing.value = follow
  selected.value = [...follow.groups]
  groupFilter.value = ''
}

function toggleSelected(groupId: number, checked: boolean) {
  selected.value = checked ? [...selected.value, groupId] : selected.value.filter((id) => id !== groupId)
}

async function saveGroups() {
  if (!editing.value) return
  editing.value.groups = [...selected.value].sort((a, b) => a - b)
  await save(editing.value)
  editing.value = null
}

onMounted(async () => {
  await Promise.all([loadStatus(), loadFollows(), loadGroups()])
})
onBeforeUnmount(stopPolling)
</script>

<template>
  <div class="flex flex-col gap-6">
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">登录</h3>
        <button class="btn btn-ghost btn-xs" aria-label="刷新登录状态" @click="loadStatus"><PhArrowsClockwise class="size-4" />刷新</button>
      </div>
      <p class="text-sm opacity-70">整个部署只登录一个 B站账号，登录状态保存在宿主浏览器中，由 B站网页每天自动检查与刷新。Super User 也可以私聊 Bot「B站登录」扫码。</p>
      <div v-if="status" class="flex flex-wrap items-center gap-3">
        <span class="badge" :class="STATE_CLASS[status.state]">{{ STATE_LABEL[status.state] }}</span>
        <span v-if="status.account">{{ status.account.name }}<span class="ml-1 text-xs opacity-60 tabular-nums">UID {{ status.account.uid }}</span></span>
        <span v-if="status.riskUntil" class="text-sm text-warning-strong">遇到风控，动态推送暂停至 {{ formatTime(status.riskUntil) }}</span>
      </div>
      <button class="btn btn-primary btn-sm w-fit" :disabled="busy" @click="startLogin()"><PhQrCode class="size-4" />扫码登录</button>
      <!-- 挂到 body 下：留在插件页面内时处于内容区的层叠上下文，遮罩盖不住侧边栏与日志面板。 -->
      <Teleport to="body">
        <div v-if="loginOpen" class="modal modal-open" role="dialog" aria-labelledby="bili-login-title">
          <div class="modal-box flex w-96 flex-col items-center gap-4">
            <header class="flex w-full items-center">
              <h3 id="bili-login-title" class="text-lg font-semibold">{{ replacing ? '更换登录账号' : '扫码登录' }}</h3>
              <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="closeLogin"><PhX class="size-4" /></button>
            </header>
            <template v-if="replacing">
              <p class="text-sm">当前已登录「{{ replacing.name }}」（UID {{ replacing.uid }}），退出该账号并重新登录？退出后原账号在 B站的登录也会失效。</p>
              <div class="flex w-full justify-end gap-2">
                <button class="btn btn-ghost btn-sm" :disabled="busy" @click="closeLogin">取消</button>
                <button class="btn btn-primary btn-sm" :disabled="busy" @click="startLogin(true)">
                  <span v-if="busy" class="loading loading-spinner loading-xs"></span>退出并重新登录
                </button>
              </div>
            </template>
            <img v-if="qr && qrState === 'Waiting'" :src="qr.image" class="size-48" alt="B站登录二维码" />
            <p
              class="text-center text-sm"
              :class="{ 'text-error-strong': qrState === 'Failed' || qrState === 'Expired', 'text-success-strong': qrState === 'Succeeded' }"
            >
              {{ qrState ? QR_LABEL[qrState] : '' }}<template v-if="qrState === 'Succeeded' && status?.account">：{{ status.account.name }}</template>
            </p>
            <span v-if="qrState === 'Scanned'" class="loading loading-spinner loading-sm"></span>
            <button v-if="qrState === 'Expired' || qrState === 'Failed'" class="btn btn-primary btn-sm" @click="startLogin()"><PhQrCode class="size-4" />重新生成</button>
          </div>
        </div>
      </Teleport>
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">关注列表</h3>
        <button class="btn btn-ghost btn-xs" :disabled="refreshing" aria-label="从 B站刷新关注列表" @click="refreshFollows">
          <span v-if="refreshing" class="loading loading-spinner loading-xs"></span>
          <PhArrowsClockwise v-else class="size-4" />刷新
        </button>
      </div>
      <p class="text-sm opacity-70">只显示当前登录账号关注的 UP，关注与取关请在 B站完成。推送设置按 UP 保存：换号后新账号也关注的 UP 自动沿用；取关的 UP 重置为未推送。新关注默认不推送，可以只推动态或只推直播。</p>
      <div>
        <table class="table table-sm">
          <thead>
            <tr><th>UP</th><th class="w-40">最近动态</th><th class="w-20">动态</th><th class="w-20">直播</th><th>推送的群</th></tr>
          </thead>
          <tbody>
            <tr v-for="follow in follows" :key="follow.uid">
              <td>
                <div class="flex items-center gap-2">
                  <img v-if="follow.face" :src="follow.face" referrerpolicy="no-referrer" loading="lazy" class="size-8 rounded-full" alt="" />
                  <span>{{ follow.name }}<span class="ml-1 text-xs opacity-60 tabular-nums">{{ follow.uid }}</span></span>
                </div>
              </td>
              <td class="text-xs tabular-nums">{{ formatTime(follow.lastDynamicAt) }}</td>
              <td><input v-model="follow.pushDynamic" type="checkbox" class="toggle toggle-sm" :aria-label="`推送 ${follow.name} 的动态`" @change="save(follow)" /></td>
              <td><input v-model="follow.pushLive" type="checkbox" class="toggle toggle-sm" :aria-label="`推送 ${follow.name} 的直播`" @change="save(follow)" /></td>
              <td>
                <button class="btn btn-ghost btn-xs h-auto min-h-6 max-w-80 justify-start py-1 text-left font-normal" :aria-label="`选择 ${follow.name} 推送的群`" @click="openGroups(follow)">
                  <span class="[overflow-wrap:anywhere]">{{ follow.groups.length === 0 ? '选择群' : follow.groups.map((id) => groupNames.get(id) ?? id).join('、') }}</span>
                </button>
              </td>
            </tr>
            <tr v-if="follows.length === 0">
              <td colspan="5" class="py-4 text-center opacity-60">{{ status?.state === 'LoggedIn' ? '没有关注，或尚未刷新' : '登录后显示该账号的关注列表' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <Teleport to="body">
      <div v-if="editing" class="modal modal-open" role="dialog" aria-labelledby="bili-groups-title">
        <div class="modal-box flex max-h-[80vh] w-11/12 max-w-md flex-col gap-3">
          <header class="flex items-center">
            <h3 id="bili-groups-title" class="text-lg font-semibold">推送「{{ editing.name }}」的群</h3>
            <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="editing = null"><PhX class="size-4" /></button>
          </header>
          <input v-model="groupFilter" class="input input-sm w-full" placeholder="按群名或群号筛选" aria-label="筛选群" />
          <ul class="min-h-0 flex-1 overflow-y-auto">
            <li v-for="group in filteredGroups" :key="group.id">
              <label class="flex cursor-pointer items-center gap-3 rounded-box px-2 py-1.5 hover:bg-base-200">
                <input type="checkbox" class="checkbox checkbox-sm" :checked="selected.includes(group.id)" @change="toggleSelected(group.id, ($event.target as HTMLInputElement).checked)" />
                <span class="[overflow-wrap:anywhere]">{{ group.name ?? '（Bot 已不在该群）' }}<span class="ml-1 text-xs opacity-60 tabular-nums">{{ group.id }}</span></span>
              </label>
            </li>
            <li v-if="filteredGroups.length === 0" class="py-4 text-center text-sm opacity-60">{{ groups.length === 0 ? '没有群（Bot 未连接）' : '没有匹配的群' }}</li>
          </ul>
          <footer class="flex items-center gap-2">
            <span class="text-sm opacity-70 tabular-nums">已选 {{ selected.length }} 个</span>
            <button class="btn btn-ghost btn-sm ml-auto" @click="editing = null">取消</button>
            <button class="btn btn-primary btn-sm" @click="saveGroups">保存</button>
          </footer>
        </div>
      </div>
    </Teleport>
  </div>
</template>
