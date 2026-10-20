import { defineComponent, h, ref } from 'vue'

// 测试用专有页面：调用插件 API 并显示结果。
export default defineComponent({
  props: { context: { type: Object, required: true } },
  setup(props) {
    const reply = ref('')
    props.context.api('/ping').then(async (response) => { reply.value = await response.text() })
    return () => h('div', { class: 'card bg-base-100' }, [h('div', { class: 'card-body' }, `sample-b: ${reply.value}`)])
  }
})
