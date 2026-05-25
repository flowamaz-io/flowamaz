import type { Config } from 'tailwindcss';

// Tailwind v4 reads its design tokens from the `@theme` block in src/assets/main.css.
// This file is retained for editor tooling / IntelliSense and to document the content globs.
// Brand tokens (single source of truth) live in main.css:
//   primary (teal #1D9E75) · accent (purple #534AB7) · amber (#EF9F27) · danger (#E24B4A)
export default {
  content: ['./index.html', './src/**/*.{vue,ts}'],
} satisfies Config;
