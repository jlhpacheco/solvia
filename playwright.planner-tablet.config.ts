import { defineConfig } from '@playwright/test';
import tablet from './playwright.tablet.config';

// Reuse the offline isolated app and existing iPad viewport/touch projects.
// SOLVIA_TABLET_WEBKIT=1 also enables WebKit; this is browser emulation,
// not evidence from physical iPad hardware.
export default defineConfig({
  ...tablet,
  testDir: './tests/e2e',
  testMatch: 'planner-date-range.spec.ts',
  outputDir: '.playwright-data/planner-tablet-results',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '.playwright-data/planner-tablet-report' }]]
});
