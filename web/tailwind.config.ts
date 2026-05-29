import type { Config } from 'tailwindcss';

// Tailwind v4 reads its design tokens from the `@theme` block in src/assets/main.css.
// This file is retained for editor tooling / IntelliSense and to document the content globs.
// Brand tokens (single source of truth) live in main.css:
//   primary (teal #1D9E75) · accent (purple #534AB7) · amber (#EF9F27) · danger (#E24B4A)
// Sidebar tokens (also declared in main.css @theme as --color-sidebar-*) are mirrored here
// for IntelliSense: bg #0F1117 · border #1C1F26 · active #1D9E75 · hover rgba(255,255,255,0.05).
export default {
  content: ['./index.html', './src/**/*.{vue,ts}'],
  theme: {
    extend: {
      colors: {
        sidebar: {
          bg: '#0F1117',
          border: '#1C1F26',
          active: '#1D9E75',
          hover: 'rgba(255,255,255,0.05)',
          text: {
            primary: '#FFFFFF',
            secondary: '#94A3B8',
            muted: '#475569',
          },
        },
      },
    },
  },
} satisfies Config;
