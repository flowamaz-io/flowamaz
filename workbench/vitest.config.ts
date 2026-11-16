import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';

// @flowamaz/canvas is used from its sibling folder's source (web/packages/canvas here, canvas/ in the public repo).
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      { find: /^@flowamaz\/canvas$/, replacement: fileURLToPath(new URL('../canvas/src/index.ts', import.meta.url)) },
      { find: /^@flowamaz\/canvas\/(.*)$/, replacement: fileURLToPath(new URL('../canvas/src/', import.meta.url)) + '$1' },
    ],
  },
  test: { globals: true, environment: 'jsdom', include: ['tests/**/*.test.ts'] },
});
