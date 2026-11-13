import { defineConfig, devices } from '@playwright/test';

/**
 * Phase 1 Playwright config (Chromium only).
 *
 * baseURL defaults to the Vite dev server (http://localhost:5173) but can be overridden
 * for a Docker/proxy run via E2E_BASE_URL (e.g. http://localhost). The factories talk to
 * the backend directly via E2E_API_URL (default http://localhost:5000). scripts/e2e-local.sh sets
 * both for a local run; .github/workflows/e2e.yml does the same in CI.
 */
export default defineConfig({
  testDir: './',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 1,
  workers: 1,
  // list for the terminal; the HTML report and e2e-results.json are what CI uploads.
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'e2e-results.json' }],
  ],
  use: {
    baseURL: process.env.E2E_BASE_URL || 'http://localhost:5173',
    // A Chromium already on the machine (e.g. a container image) instead of Playwright's own download.
    ...(process.env.E2E_CHROMIUM_PATH ? { launchOptions: { executablePath: process.env.E2E_CHROMIUM_PATH } } : {}),
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
