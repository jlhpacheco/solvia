import { defineConfig, devices } from '@playwright/test';

// This suite uses the real running app and an explicitly selected real provider.
// It is separate from the isolated, provider-free browser test suite.
if (process.env.SOLVIA_RUN_LIVE_AI !== '1') {
  throw new Error('Set SOLVIA_RUN_LIVE_AI=1 to opt in to real-model tests. Meta requests may incur charges.');
}
const provider = process.env.SOLVIA_AI_PROVIDER;
if (provider !== 'ollama' && provider !== 'meta') {
  throw new Error('Set SOLVIA_AI_PROVIDER explicitly to ollama or meta.');
}

const baseURL = process.env.SOLVIA_AI_BASE_URL ?? 'http://localhost:5156';
const appAddress = new URL(baseURL);
if (!['http:', 'https:'].includes(appAddress.protocol) ||
    !['localhost', '127.0.0.1', '[::1]'].includes(appAddress.hostname)) {
  throw new Error('Live AI checks require a loopback Solvia app.');
}

export default defineConfig({
  testDir: './tests/ai',
  testMatch: 'model-live.spec.ts',
  metadata: { aiProvider: provider },
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 150_000,
  expect: { timeout: 10_000 },
  reporter: [
    ['list'],
    ['json', { outputFile: `.playwright-data/ai-evidence/${provider}/results.json` }]
  ],
  outputDir: `.playwright-data/ai-evidence/${provider}/artifacts`,
  use: {
    ...devices['Desktop Chrome'],
    baseURL,
    trace: 'off',
    screenshot: 'only-on-failure'
  }
});
