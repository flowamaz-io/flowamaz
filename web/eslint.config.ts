import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import pluginVue from 'eslint-plugin-vue';
import vueParser from 'vue-eslint-parser';
import globals from 'globals';

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'playwright-report', 'coverage'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  {
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
    },
  },
  {
    files: ['**/*.vue'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
      },
    },
  },
  {
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      'vue/multi-word-component-names': 'off',
      // Optional props use `?` + withDefaults idiomatically; explicit defaults add noise.
      'vue/require-default-prop': 'off',
      // A leading underscore marks a deliberately-unused binding (placeholder args, reserved
      // destructure targets) — the conventional escape hatch, so don't flag those.
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
      }],
    },
  },
  {
    // ArticleRenderer / HelpSearch render trusted, bundled markdown (our own docs) — v-html is safe.
    files: ['src/components/help/ArticleRenderer.vue', 'src/components/help/HelpSearch.vue'],
    rules: { 'vue/no-v-html': 'off' },
  },
);
