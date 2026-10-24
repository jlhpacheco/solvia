import { defineConfig } from '@playwright/test';
import tablet from './playwright.tablet.config';

// Reuse the offline isolated app and existing iPad viewport/touch projects.
// SOLVIA_TABLET_WEBKIT=1 also enables WebKit; this is browser emulation,
// not evidence from physical iPad hardware.
export default defineConfig({
  ...tablet,
  testDir: './tests',
  testMatch: ['**/planner-date-range.spec.ts', '**/innovation.spec.ts'],
  // Run range/history/no-JS cases plus the two touch/layout planner journeys.
  // Operations and the independent AI disclosure are unchanged by this scope.
  grepInvert: /AI observation disclosure|read-only payment review/,
  outputDir: '.playwright-data/planner-tablet-results',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '.playwright-data/planner-tablet-report' }]]
});
