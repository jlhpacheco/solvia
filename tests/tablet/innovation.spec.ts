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

test('one-visit selection is touch-operable without recurrence fields or inference on the tablet', async ({ page }) => {
  const rejected: string[] = [];
  await page.route(/\/Planner\/Read(?:[/?]|$)|\/Permits\/Understand(?:[/?]|$)|\/api\/(?:chat|paypal)(?:[/?]|$)|\/Checkout(?:[/?]|$)|https?:\/\/(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)(?:[/:]|$)|https?:\/\/(?:localhost|127\.0\.0\.1):11434(?:[/?]|$)/i, async route => {
    rejected.push(`${route.request().method()} ${new URL(route.request().url()).pathname}`);
    await route.abort();
  });
  const date = `${new Date().getFullYear() + 1}-01-20`;
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByLabel('Spring', { exact: true }).tap();
  await page.getByRole('radio', { name: 'One visit', exact: true }).tap();
  await page.getByLabel('Visit date', { exact: true }).fill(date);
  await expect(page.getByRole('radio', { name: 'One visit', exact: true })).toBeChecked();
  await expect(page.locator('#repeat-weeks')).toBeHidden();
  await expect(page.locator('#repeat-weekdays')).toBeHidden();
  await expect(page.locator('#Weeks')).toBeDisabled();
  await expect(page.locator('#day-Monday')).toBeDisabled();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).tap();
  await expect(page.getByTestId('plan-result')).toContainText('1 visit. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(date);
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  expect(rejected).toEqual([]);
});
