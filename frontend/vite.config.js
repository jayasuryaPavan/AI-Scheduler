import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      // Proxy /api/* to the .NET backend during development
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true
      }
    }
  },
  build: {
    // Build output goes into backend/wwwroot so the .NET app serves the SPA
    outDir: '../backend/wwwroot',
    emptyOutDir: true
  }
})
