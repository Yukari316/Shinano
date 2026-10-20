<script setup lang="ts">
import type { JsonSchema } from '../types'
import SchemaField from './SchemaField.vue'
import { orderedProperties } from './schema'

defineProps<{ schema: JsonSchema }>()
const model = defineModel<Record<string, unknown>>({ required: true })

function update(key: string, value: unknown): void {
  model.value = { ...model.value, [key]: value }
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <SchemaField
      v-for="[key, child] in orderedProperties(schema)"
      :key="key"
      :name="key"
      :schema="child"
      :required="schema.required?.includes(key)"
      :model-value="model[key]"
      @update:model-value="update(key, $event)"
    />
  </div>
</template>
