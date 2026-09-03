import path from 'path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/ — `defineConfig` comes from vitest/config, which is
// vite's own plus the `test` block below.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  test: {
    // The components under test render into a DOM and read localStorage.
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Tests live beside the code they cover.
    include: ['src/**/*.test.{ts,tsx}'],
  },
  server: {
    port: 5173,
    proxy: {
      // The API Gateway, and nothing else. Every /api call the app makes goes
      // through this one target: the gateway validates the bearer token and
      // proxies on to whichever of the five services owns the path. Pointing
      // this at a single service again would bypass that.
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
