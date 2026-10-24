import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // The app calls relative /api/... URLs (VITE_API_BASE_URL is empty by default), so the dev
    // server must forward them to the API — without this every browser call 404s locally.
    proxy: {
      '/api': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8307', changeOrigin: true },
      '/webhooks': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8307', changeOrigin: true },
      '/scalar': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8307', changeOrigin: true },
    },
  },
});
