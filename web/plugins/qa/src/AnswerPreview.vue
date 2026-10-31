<script setup lang="ts">
import { PLUGIN_API_PREFIX } from '@shinano/plugin-sdk'
import type { AnswerPart } from './types'

const props = defineProps<{ answer: AnswerPart[]; pluginId: string }>()

function mediaUrl(part: AnswerPart): string {
  return `${PLUGIN_API_PREFIX}/${encodeURIComponent(props.pluginId)}/media/${part.mediaId}`
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-1 gap-y-2 text-sm [overflow-wrap:anywhere]">
    <template v-for="(part, index) in answer" :key="index">
      <span v-if="part.kind === 'Text'" class="whitespace-pre-wrap">{{ part.text }}</span>
      <span v-else-if="part.kind === 'Mention'" class="badge badge-ghost badge-sm">@{{ part.userId }}</span>
      <span v-else-if="part.kind === 'Face'" class="badge badge-ghost badge-sm">[表情 {{ part.faceId }}]</span>
      <a v-else-if="part.kind === 'Image'" :href="mediaUrl(part)" target="_blank" rel="noopener">
        <img :src="mediaUrl(part)" alt="回答图片" class="max-h-28 rounded-box border border-[var(--line)]" loading="lazy" />
      </a>
      <audio v-else-if="part.kind === 'Audio'" :src="mediaUrl(part)" controls preload="none" class="h-9"></audio>
      <video v-else-if="part.kind === 'Video'" :src="mediaUrl(part)" controls preload="none" class="max-h-40 rounded-box"></video>
      <a v-else-if="part.kind === 'File'" :href="mediaUrl(part)" :download="part.fileName" class="link">[文件 {{ part.fileName }}]</a>
    </template>
  </div>
</template>
