import { fileURLToPath } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// 监控进程默认端口，开发服务器把状态接口代理到本机运行的监控进程。
const MONITOR_ORIGIN = 'http://127.0.0.1:19101'

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  build: {
    outDir: fileURLToPath(new URL('../../src/Shinano.Monitor/wwwroot', import.meta.url)),
    emptyOutDir: true
  },
  server: {
    proxy: {
      '/api': MONITOR_ORIGIN
    }
  }
})
