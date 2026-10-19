import { defineConfig } from '@playwright/test';

// Explicit opt-in integration run. Never part of the ordinary isolated E2E suite.
export default defineConfig({
  testDir: './tests/sandbox',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['json', { outputFile: 'sandbox-results/results.json' }]],
  outputDir: 'sandbox-results/artifacts',
  use: {
    baseURL: 'http://localhost:5156',
    viewport: { width: 1400, height: 1050 },
    trace: 'off', // Provider credentials must never be recorded in browser traces.
    screenshot: 'only-on-failure'
  }
});
