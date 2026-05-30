import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/tests/setup.ts'],
    include: ['src/**/*.{test,spec}.ts'],
    // Pin the API base so axios builds absolute URLs that match the MSW handlers
    // (handlers are registered against http://localhost:5000). Without this the base is
    // empty, requests resolve relative to the jsdom origin, and MSW never intercepts them.
    env: {
      VITE_API_BASE_URL: 'http://localhost:5000',
    },
  },
});
