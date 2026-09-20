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
    // `npm run test:coverage` (US-33). V8's own instrumentation, not Istanbul —
    // no source transform, so the numbers match what actually ran.
    coverage: {
      provider: 'v8',
      // text: printed in the job log. html: browsable, uploaded as a CI
      // artifact. lcov + json-summary: machine-readable, for tooling that
      // wants a number without parsing HTML.
      reporter: ['text', 'html', 'lcov', 'json-summary'],
      reportsDirectory: './coverage',
      // Test helpers and the app's bootstrap file are not logic to measure —
      // everything else under src/ is included, ui/ primitives included, since
      // the existing page tests already exercise them for real.
      exclude: ['src/test/**', 'src/main.tsx'],
    },
  },
  server: {
    port: 5173,
    proxy: {
      // The API Gateway, and nothing else. Every /api call the app makes goes
      // through this one target: the gateway validates the bearer token and
      // proxies on to whichever of the five services owns the path. Pointing
      // this at a single service again would bypass that.
      '/api': {
        // LOCAL-ONLY WORKAROUND — do not commit. Gateway moved to 5100 on this
        // machine because macOS AirPlay Receiver holds port 5000. Revert to
        // 5000 (or disable AirPlay Receiver) to match the team's default.
        target: 'http://localhost:5100',
        changeOrigin: true,
      },
    },
  },
})
