import { defineConfig } from 'vite'

// 插件 SDK 构建为单个浏览器 ESM 文件，由宿主经 import map 以 @shinano/plugin-sdk 提供给插件页面。
export default defineConfig({
  build: {
    lib: {
      entry: 'src/index.ts',
      formats: ['es'],
      fileName: () => 'plugin-sdk.js'
    },
    rollupOptions: {
      external: ['vue']
    }
  }
})
