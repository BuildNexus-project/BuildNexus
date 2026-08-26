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
      // The User Service during local development. Once the YARP gateway is up,
      // this target becomes the gateway instead.
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
    },
  },
})
