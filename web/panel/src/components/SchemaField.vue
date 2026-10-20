<script setup lang="ts">
import { computed, ref } from 'vue'
import type { JsonSchema } from '../types'
import { controlOf, orderedProperties, primaryType } from './schema'

defineOptions({ name: 'SchemaField' })

const props = defineProps<{ name: string; schema: JsonSchema; required?: boolean }>()
const model = defineModel<unknown>({ required: true })

const control = computed(() => controlOf(props.schema))
const label = computed(() => props.schema.title ?? props.name)
const itemType = computed(() => (props.schema.items ? primaryType(props.schema.items) : undefined))

// 列表按行编辑，数字列表忽略无法解析的行。
const listText = computed({
  get: () => (Array.isArray(model.value) ? model.value.join('\n') : ''),
  set: (text: string) => {
    const lines = text.split('\n').map((line) => line.trim()).filter((line) => line.length > 0)
    model.value = itemType.value === 'string' ? lines : lines.map(Number).filter((value) => !Number.isNaN(value))
  }
})

const jsonText = ref(JSON.stringify(model.value ?? null, null, 2))
const jsonError = ref('')
function updateJson(text: string): void {
  jsonText.value = text
  try {
    model.value = JSON.parse(text)
    jsonError.value = ''
  } catch {
    jsonError.value = 'JSON 格式不正确'
  }
}

const objectValue = computed(() => (model.value ?? {}) as Record<string, unknown>)
function updateChild(key: string, value: unknown): void {
  model.value = { ...objectValue.value, [key]: value }
}
</script>

<template>
  <fieldset v-if="control === 'object'" class="fieldset rounded-box border border-base-300 p-4">
    <legend class="fieldset-legend">{{ label }}</legend>
    <p v-if="schema.description" class="label whitespace-normal">{{ schema.description }}</p>
    <SchemaField
      v-for="[key, child] in orderedProperties(schema)"
      :key="key"
      :name="key"
      :schema="child"
      :required="schema.required?.includes(key)"
      :model-value="objectValue[key]"
      @update:model-value="updateChild(key, $event)"
    />
  </fieldset>

  <fieldset v-else class="fieldset">
    <legend class="fieldset-legend">
      {{ label }}<span v-if="required" class="text-error">*</span>
    </legend>
    <input v-if="control === 'boolean'" v-model="model" type="checkbox" class="toggle toggle-primary" />
    <select v-else-if="control === 'enum'" v-model="model" class="select w-full max-w-md">
      <option v-for="option in schema.enum" :key="String(option)" :value="option">{{ option ?? '（空）' }}</option>
    </select>
    <input
      v-else-if="control === 'integer' || control === 'number'"
      v-model.number="model"
      type="number"
      class="input w-full max-w-md"
      :min="schema.minimum"
      :max="schema.maximum"
      :step="control === 'integer' ? 1 : 'any'"
      :required="required"
    />
    <input
      v-else-if="control === 'string'"
      v-model="model"
      type="text"
      class="input w-full max-w-md"
      :minlength="schema.minLength"
      :maxlength="schema.maxLength"
      :pattern="schema.pattern"
      :required="required"
    />
    <textarea
      v-else-if="control === 'list'"
      v-model="listText"
      class="textarea w-full max-w-md font-mono"
      rows="4"
      placeholder="每行一项"
    />
    <template v-else>
      <textarea
        :value="jsonText"
        class="textarea w-full font-mono"
        rows="6"
        @input="updateJson(($event.target as HTMLTextAreaElement).value)"
      />
      <p v-if="jsonError" class="label text-error">{{ jsonError }}</p>
    </template>
    <p v-if="schema.description" class="label whitespace-normal">{{ schema.description }}</p>
    <p v-if="schema.minimum !== undefined || schema.maximum !== undefined" class="label">
      范围 {{ schema.minimum ?? '−∞' }} – {{ schema.maximum ?? '∞' }}
    </p>
  </fieldset>
</template>
