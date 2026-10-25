import { defineConfig, devices } from '@playwright/test';
import base from './playwright.config';

// Additional Apple-device viewport/touch emulation; not physical device evidence.
export default defineConfig({
  ...base,
  testMatch: 'prepaid.spec.ts',
  webServer: { ...base.webServer!, command: 'dotnet run --project src/Solvia --no-restore --no-launch-profile -p:OutputPath=bin/PrepaidApple --urls http://127.0.0.1:5158', url: 'http://127.0.0.1:5158' },
  use: { ...base.use, baseURL: 'http://127.0.0.1:5158' },
  outputDir: '.playwright-data/prepaid-apple-results',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '.playwright-data/prepaid-apple-report' }]],
  projects: [
    { name: 'iphone-emulated-webkit', use: { ...devices['iPhone 13'], browserName: 'webkit' } },
    { name: 'ipad-emulated-webkit', use: { ...devices['iPad (gen 7)'], browserName: 'webkit' } }
  ]
});
