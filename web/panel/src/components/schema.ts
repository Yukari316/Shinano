import type { JsonSchema } from '../types'

/** 去掉可空类型中的 null，返回主要类型。 */
export function primaryType(schema: JsonSchema): string | undefined {
  const types = Array.isArray(schema.type) ? schema.type : schema.type ? [schema.type] : []
  return types.find((type) => type !== 'null')
}

/** 表单控件种类；无法用表单表达的类型退化为 JSON 文本。 */
export type FieldControl = 'boolean' | 'integer' | 'number' | 'enum' | 'string' | 'list' | 'object' | 'json'

export function controlOf(schema: JsonSchema): FieldControl {
  if (schema.enum) return 'enum'
  switch (primaryType(schema)) {
    case 'boolean':
      return 'boolean'
    case 'integer':
      return 'integer'
    case 'number':
      return 'number'
    case 'string':
      return 'string'
    case 'array': {
      const item = schema.items ? primaryType(schema.items) : undefined
      return item === 'string' || item === 'integer' || item === 'number' ? 'list' : 'json'
    }
    case 'object':
      return schema.properties ? 'object' : 'json'
    default:
      return 'json'
  }
}

/** 按 `x-order` 排序的属性，没有 `x-order` 的保持声明顺序并排在后面。 */
export function orderedProperties(schema: JsonSchema): [string, JsonSchema][] {
  return Object.entries(schema.properties ?? {})
    .map((entry, index) => ({ entry, index }))
    .sort((left, right) => {
      const leftOrder = left.entry[1]['x-order'] ?? Number.MAX_SAFE_INTEGER
      const rightOrder = right.entry[1]['x-order'] ?? Number.MAX_SAFE_INTEGER
      return leftOrder - rightOrder || left.index - right.index
    })
    .map((item) => item.entry)
}
