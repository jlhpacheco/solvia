import { defineConfig, devices } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const dataDirectory = path.resolve(__dirname, '.playwright-data');
mkdirSync(dataDirectory, { recursive: true });
const database = path.join(dataDirectory, `solvia-${randomUUID()}.db`);

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:5157',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  projects: [
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile-chromium', use: { ...devices['Pixel 7'] } }
  ],
  webServer: {
    // Separate output, port, environment and database preserve the user's preview.
    command: 'dotnet run --project src/Solvia --no-restore --no-launch-profile -p:OutputPath=bin/E2E --urls http://127.0.0.1:5157',
    url: 'http://127.0.0.1:5157',
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Testing',
      MSBuildEnableWorkloadResolver: 'false',
      ConnectionStrings__SolviaDb: `Data Source=${database}`,
      OLLAMA_BASE_URL: 'http://127.0.0.1:1',
      Ollama__BaseUrl: 'http://127.0.0.1:1',
      AI_PROVIDER: 'ollama',
      AI__Provider: 'ollama',
      TOGETHER_API_KEY: '',
      Together__ApiKey: '',
      // Keep the supported model valid even when the hosted provider is off.
      // Provider selection and both empty keys below/above still force offline tests.
      TOGETHER_MODEL: 'openai/gpt-oss-120b',
      Together__Model: 'openai/gpt-oss-120b',
      TOGETHER_TIMEOUT_SECONDS: '',
      Together__TimeoutSeconds: '10',
      PAYPAL_CLIENT_ID: '',
      PAYPAL_CLIENT_SECRET: '',
      PayPal__ClientId: '',
      PayPal__ClientSecret: '',
      WEBHOOK_URL: '',
      Logging__LogLevel__Default: 'Warning'
    }
  }
});
