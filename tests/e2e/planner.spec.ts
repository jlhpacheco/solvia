import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

function planWindow() {
  const now = new Date();
  const year = now.getFullYear();
  const fall = new Date(year, 7, 24);
  const latestSafe = new Date(year, 10, 1);
  const date = now < fall ? fall : now < latestSafe ? now : new Date(year + 1, 0, 20);
  const iso = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  return { iso, term: date.getMonth() < 5 ? 'Spring' : 'Fall' };
}

async function configure(page: Page) {
  const window = planWindow();
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByLabel('Start of your planning period').fill(window.iso);
  await page.getByLabel('Number of weeks', { exact: true }).fill('3');
  await page.getByLabel(window.term, { exact: true }).check();
  return window;
}

test('@smoke planner compares confirmed costs with no automatic purchase', async ({ page }) => {
  await configure(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  const results = page.getByTestId('plan-result');
  await expect(results).toContainText('6 visits. Two ways to plan.');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('#comparison-title')).toBeFocused();
  await expect(page.getByTestId('daily-total')).toHaveText('$6.00');
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await expect(results).toContainText('only the first planned day');
  await page.getByText('Visit dates and price source', { exact: true }).click();
  await expect(results).toContainText('not a live campus feed');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test('schedule reader is honestly labeled and editable before calculation', async ({ page }) => {
  await configure(page);
  await page.getByLabel('Describe your visits', { exact: false }).fill('Tuesdays and Thursdays for three weeks');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expect(page.getByLabel('Tuesday', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Thursday', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('3');
  await page.getByLabel('Wednesday', { exact: true }).check();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('9 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$9.00');
});

test('uncertain schedule leaves choices open for confirmation', async ({ page }) => {
  await configure(page);
  await page.getByLabel('Describe your visits', { exact: false }).fill('Maybe Tuesdays or Thursdays, not sure how many weeks');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByLabel('Tuesday', { exact: true })).not.toBeChecked();
  await expect(page.getByLabel('Thursday', { exact: true })).not.toBeChecked();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('');
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
});

test('one-day continuation keeps its planned date through vehicle review and Fix it', async ({ page }) => {
  await configure(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  const date = await page.locator('input[name="startDate"]').inputValue();
  await expect(page.getByLabel('Day pass — one day', { exact: true })).toBeChecked();
  await expect(page.locator('#term-options')).toBeHidden();
  await page.getByLabel(/Your car/).fill(`blue Honda Civic, license plate P${randomUUID().slice(0, 6)}`);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.locator('input[name="StartDate"]')).toHaveValue(date);
  await page.getByRole('button', { name: 'Fix it — back to the questions', exact: true }).click();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(date);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('$1.00');
});

test('unpublished term stays unavailable while published day estimate is shown', async ({ page }) => {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('CSU Bakersfield');
  await page.getByLabel('Start of your planning period').fill(`${new Date().getFullYear() + 1}-06-08`);
  await page.getByLabel('Summer', { exact: true }).check();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('term-total')).toHaveText('Not published');
  await expect(page.getByRole('button', { name: 'Choose a term permit', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Start with one day pass', exact: true })).toBeVisible();
  await expect(page.getByTestId('plan-result')).toContainText('no cheapest-option claim');
});

test('employee comparison applies the server-resolved demo exemption', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Elena', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome, Elena.', exact: true })).toBeVisible();
  await configure(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('daily-total')).toHaveText('$0.00');
  await expect(page.getByTestId('term-total')).toHaveText('$0.00');
  await expect(page.getByTestId('plan-result')).toContainText('Employee exemption');
});

test('planner comparison remains usable without JavaScript', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL });
  try {
    const page = await context.newPage();
    await configure(page);
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await expect(page.getByTestId('daily-total')).toHaveText('$6.00');
    await page.getByRole('button', { name: 'Choose a term permit', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
  } finally { await context.close(); }
});

test('future issued permit retains its PDF without claiming current validity', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Elena', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome, Elena.', exact: true })).toBeVisible();
  await page.goto('/Planner');
  await page.getByLabel('Start of your planning period').fill(`${new Date().getFullYear() + 1}-08-24`);
  await page.getByLabel('Fall', { exact: true }).check();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  const plate = 'F' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.getByLabel(/Your car/).fill(`white Honda Civic, license plate ${plate}`);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Permit issued', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('not valid yet');
  await expect(page.getByRole('link', { name: 'Download permit (PDF)', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Back to my permits', exact: true }).click();
  const card = page.locator('.d-xxl-none .sv-card').filter({ hasText: plate });
  await expect(card).toContainText('Issued · inactive');
  await expect(card.getByText('Live', { exact: true })).toHaveCount(0);
  await expect(card.getByRole('link', { name: 'Permit (PDF)', exact: true })).toBeVisible();
});
