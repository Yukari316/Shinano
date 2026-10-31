<script setup lang="ts">
import { PhArrowDown, PhArrowUp, PhAt, PhPaperclip, PhTextT, PhX } from '@phosphor-icons/vue'
import type { PluginPageContext } from '@shinano/plugin-sdk'
import { computed, ref } from 'vue'
import AnswerPreview from './AnswerPreview.vue'
import type { DraftPart, Entry, Group, MatchType } from './types'

const props = defineProps<{ context: PluginPageContext; groups: Group[] }>()
const emit = defineEmits<{ saved: [] }>()

const groupId = ref<number | null>(props.groups[0]?.groupId ?? null)
const matchType = ref<MatchType>('Full')
const question = ref('')
// 回答默认为空，按需添加文字、@ 或文件，纯图片或纯文件的回答不必先删去占位。
const parts = ref<DraftPart[]>([])
const saving = ref(false)
const error = ref('')
// 问题已存在时显示原词条，确认后带上它的版本再次提交即覆盖。
const duplicate = ref<Entry | null>(null)
let nextKey = 0

const canSubmit = computed(() => groupId.value !== null && question.value.trim().length > 0 && parts.value.length > 0 && !saving.value)

function add(type: DraftPart['type']) {
  const key = nextKey++
  parts.value.push(type === 'Text' ? { key, type, text: '' } : type === 'Mention' ? { key, type, userId: '' } : { key, type, file: null })
}

function move(index: number, offset: number) {
  const target = index + offset
  if (target < 0 || target >= parts.value.length) return
  const list = [...parts.value]
  const [item] = list.splice(index, 1)
  if (item) list.splice(target, 0, item)
  parts.value = list
}

function onFile(part: DraftPart, event: Event) {
  if (part.type === 'Upload') part.file = (event.target as HTMLInputElement).files?.[0] ?? null
}

async function submit(replaceVersion?: number) {
  if (!canSubmit.value || groupId.value === null) return
  saving.value = true
  error.value = ''
  const form = new FormData()
  form.set('groupId', String(groupId.value))
  form.set('matchType', matchType.value)
  form.set('question', question.value.trim())
  if (replaceVersion !== undefined) form.set('replaceVersion', String(replaceVersion))
  const payload = parts.value.map((part, index) => {
    if (part.type === 'Text') return { type: 'Text', text: part.text }
    if (part.type === 'Mention') return { type: 'Mention', userId: Number(part.userId) }
    const field = `file${index}`
    if (part.file) form.append(field, part.file)
    return { type: 'Upload', field }
  })
  form.set('parts', JSON.stringify(payload))

  try {
    const response = await props.context.api('/entries', { method: 'POST', body: form })
    if (response.ok) {
      duplicate.value = null
      question.value = ''
      parts.value = []
      emit('saved')
      return
    }

    const body = (await response.json().catch(() => ({}))) as { error?: string; existing?: Entry }
    if (response.status === 409 && body.error === 'duplicate' && body.existing) duplicate.value = body.existing
    else if (response.status === 409) {
      duplicate.value = null
      error.value = '这个问题刚被别人修改或删除了，请重新提交'
    } else error.value = body.error ?? `保存失败（${response.status}）`
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <form class="tile flex flex-col gap-3 p-4" @submit.prevent="submit()">
    <div class="grid gap-3 md:grid-cols-[minmax(0,16rem)_auto_minmax(0,1fr)]">
      <label class="flex flex-col gap-1 text-sm">
        群
        <select v-model="groupId" class="select select-sm w-full" required>
          <option v-if="groups.length === 0" :value="null" disabled>Bot 未连接或不在任何群</option>
          <option v-for="group in groups" :key="group.groupId" :value="group.groupId">{{ group.name }}（{{ group.groupId }}）</option>
        </select>
      </label>
      <fieldset class="flex flex-col gap-1 text-sm">
        <legend class="mb-1">匹配方式</legend>
        <div class="join">
          <input v-model="matchType" class="btn btn-sm join-item" type="radio" value="Full" aria-label="全字" />
          <input v-model="matchType" class="btn btn-sm join-item" type="radio" value="Regex" aria-label="正则" />
        </div>
      </fieldset>
      <label class="flex flex-col gap-1 text-sm">
        {{ matchType === 'Regex' ? '正则表达式' : '问题' }}
        <input v-model="question" class="input input-sm w-full" maxlength="200" required :placeholder="matchType === 'Regex' ? '如 ^(早|早上好)$' : '完整的问题文字'" />
      </label>
    </div>

    <div class="flex flex-col gap-2">
      <span class="text-sm">回答（按顺序组成）</span>
      <div v-for="(part, index) in parts" :key="part.key" class="flex items-start gap-2">
        <span class="badge badge-ghost badge-sm mt-1.5 shrink-0">{{ part.type === 'Text' ? '文字' : part.type === 'Mention' ? '@' : '文件' }}</span>
        <textarea v-if="part.type === 'Text'" v-model="part.text" class="textarea textarea-sm min-h-9 w-full" rows="2"></textarea>
        <input v-else-if="part.type === 'Mention'" v-model="part.userId" class="input input-sm w-48" inputmode="numeric" placeholder="QQ 号" />
        <input v-else type="file" class="file-input file-input-sm w-full max-w-md" @change="onFile(part, $event)" />
        <div class="flex shrink-0 gap-1">
          <button type="button" class="btn btn-ghost btn-sm btn-square" aria-label="上移" :disabled="index === 0" @click="move(index, -1)"><PhArrowUp class="size-4" /></button>
          <button type="button" class="btn btn-ghost btn-sm btn-square" aria-label="下移" :disabled="index === parts.length - 1" @click="move(index, 1)"><PhArrowDown class="size-4" /></button>
          <button type="button" class="btn btn-ghost btn-sm btn-square" aria-label="删除这一段" @click="parts.splice(index, 1)"><PhX class="size-4" /></button>
        </div>
      </div>
      <div class="flex flex-wrap gap-2">
        <button type="button" class="btn btn-sm" @click="add('Text')"><PhTextT class="size-4" />文字</button>
        <button type="button" class="btn btn-sm" @click="add('Mention')"><PhAt class="size-4" />@某人</button>
        <button type="button" class="btn btn-sm" @click="add('Upload')"><PhPaperclip class="size-4" />文件</button>
        <span class="self-center text-xs opacity-60">上传的文件按类型识别为图片、语音、视频或文件</span>
      </div>
    </div>

    <div v-if="duplicate" role="alert" class="alert alert-warning flex flex-col items-start gap-2">
      <span>本群已有这个问题，确认后用新回答覆盖：</span>
      <AnswerPreview :answer="duplicate.answer" :plugin-id="context.pluginId" />
      <div class="flex gap-2">
        <button type="button" class="btn btn-sm btn-warning" :disabled="saving" @click="submit(duplicate.version)">覆盖</button>
        <button type="button" class="btn btn-sm" @click="duplicate = null">取消</button>
      </div>
    </div>
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
    <div class="flex justify-end">
      <button type="submit" class="btn btn-primary btn-sm" :disabled="!canSubmit || duplicate !== null">{{ saving ? '保存中…' : '保存' }}</button>
    </div>
  </form>
</template>
