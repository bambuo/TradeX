import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'url'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5283',
        changeOrigin: true
      },
      '/hubs': {
        target: 'http://localhost:5283',
        changeOrigin: true,
        ws: true,
        timeout: 60000,
        proxyTimeout: 60000
      },
      '/uploads': {
        target: 'http://localhost:5283',
        changeOrigin: true
      }
    }
  }
})
