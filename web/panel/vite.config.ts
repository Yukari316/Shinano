import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, type Plugin } from 'vite'

const require = createRequire(import.meta.url)

// 宿主默认端口，开发服务器把 API 与插件页面资源代理到本机运行的宿主。
const HOST_ORIGIN = 'http://127.0.0.1:19100'

/**
 * 把 Vue 运行时与插件 SDK 作为独立文件输出。index.html 的 import map 把 `vue` 与 `@shinano/plugin-sdk`
 * 指向这两个文件，面板与插件页面因此共用同一份 Vue，插件不自带 Vue 运行时。
 */
function sharedRuntime(): Plugin {
  return {
    name: 'shinano-shared-runtime',
    apply: 'build',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'vendor/vue.js',
        source: readFileSync(require.resolve('vue/dist/vue.runtime.esm-browser.prod.js'))
      })
      this.emitFile({
        type: 'asset',
        fileName: 'sdk/plugin-sdk.js',
        source: readFileSync(fileURLToPath(new URL('../plugin-sdk/dist/plugin-sdk.js', import.meta.url)))
      })
    }
  }
}

export default defineConfig({
  plugins: [vue(), tailwindcss(), sharedRuntime()],
  build: {
    outDir: fileURLToPath(new URL('../../src/Shinano.Host/wwwroot', import.meta.url)),
    emptyOutDir: true,
    rollupOptions: {
      external: ['vue', '@shinano/plugin-sdk']
    }
  },
  server: {
    proxy: {
      '/api': HOST_ORIGIN,
      '/plugin-web': HOST_ORIGIN
    }
  }
})
