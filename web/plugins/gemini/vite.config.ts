import vue from '@vitejs/plugin-vue'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'

// Gemini 专有页面：构建为单个浏览器 ESM 文件，输出到插件包的 web/ 目录，随插件交付。
// vue 与 @shinano/plugin-sdk 由面板的 import map 提供；样式只用面板已生成的 Tailwind 与 daisyUI 类，不单独输出 CSS。
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: fileURLToPath(new URL('../../../plugins/Shinano.Plugin.Gemini/web', import.meta.url)),
    emptyOutDir: true,
    lib: {
      entry: 'src/index.ts',
      formats: ['es'],
      fileName: () => 'index.js'
    },
    rollupOptions: {
      external: ['vue', '@shinano/plugin-sdk']
    }
  }
})
