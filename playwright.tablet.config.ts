import { defineConfig, devices } from '@playwright/test';
import base from './playwright.config';

// iPad viewport/touch emulation, not a claim about physical iPad hardware.
// Matching Playwright WebKit must be installed before enabling those projects.
const orientations = [
  { name: 'portrait', device: devices['iPad (gen 7)'] },
  { name: 'landscape', device: devices['iPad (gen 7) landscape'] }
] as const;
const engines = process.env.SOLVIA_TABLET_WEBKIT === '1'
  ? ['chromium', 'webkit'] as const
  : ['chromium'] as const;

export default defineConfig({
  ...base,
  testDir: './tests/tablet',
  timeout: 60_000,
  outputDir: '.playwright-data/tablet-results',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '.playwright-data/tablet-report' }]],
  projects: engines.flatMap(browserName => orientations.map(({ name, device }) => ({
    name: `ipad-emulated-${name}-${browserName}`,
    use: { ...device, browserName }
  })))
});
