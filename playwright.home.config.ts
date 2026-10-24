import { defineConfig, devices } from '@playwright/test';
import base from './playwright.config';

// Responsive browser emulation; this is not physical iPhone/iPad certification.
export default defineConfig({
  ...base,
  testMatch: '**/home-onboarding.spec.ts',
  timeout: 60_000,
  outputDir: '.playwright-data/home-layout/artifacts',
  reporter: [['list'], ['json', { outputFile: '.playwright-data/home-layout/results.json' }]],
  projects: [
    { name: 'home-desktop-chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } },
    { name: 'home-small-desktop-chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 720 } } },
    { name: 'home-iphone-se-webkit', use: { ...devices['iPhone SE'], browserName: 'webkit' } },
    { name: 'home-iphone-13-webkit', use: { ...devices['iPhone 13'], browserName: 'webkit' } },
    { name: 'home-iphone-landscape-webkit', use: { ...devices['iPhone 13 landscape'], browserName: 'webkit' } },
    { name: 'home-ipad-webkit', use: { ...devices['iPad (gen 7)'], browserName: 'webkit' } }
  ]
});
