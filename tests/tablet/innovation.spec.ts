import { test, expect } from '@playwright/test';

test('AI observation disclosure fits and remains touch-operable on the tablet', async ({ page }) => {
  await page.goto('/Planner');
  const status = page.getByTestId('ai-operation-status');
  await expect(status).toBeVisible();
  await status.locator('summary').tap();
  await expect(status.locator('[data-ai-model]')).toContainText('Ollama');
  await expect(status).toContainText('Checking status never calls a model');
  const bounds = await status.locator('.sv-ai-status-panel').boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
  await status.locator('summary').tap();
  await expect(status).not.toHaveAttribute('open', '');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test('planner comparison and one-day handoff fit the tablet', async ({ page }) => {
  await page.goto('/Planner');
  const year = new Date().getFullYear() + 1;
  await page.getByLabel('Start of your planning period').fill(`${year}-08-24`);
  await page.getByLabel('Fall', { exact: true }).check();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('daily-total')).toHaveText('$6.00');
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(new RegExp(`^${year}-08-`));
  await expect(page.locator('#term-options')).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test('read-only payment review filters and totals fit the tablet', async ({ page }) => {
  await page.goto('/Operations');
  await expect(page.getByRole('heading', { name: 'Payment review', exact: true })).toBeVisible();
  await page.getByLabel('Payment status', { exact: true }).selectOption('Paid');
  await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
  await expect(page.getByTestId('pr-paid-amount')).toBeVisible();
  await expect(page.getByLabel('Payment status', { exact: true })).toHaveValue('Paid');
  await expect(page.getByText('PayPal was not contacted', { exact: false })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});
