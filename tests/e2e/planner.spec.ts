import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test.describe.configure({ timeout: 60_000 });

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

function nextPlanningMonday(iso: string) {
  const date = new Date(`${iso}T12:00:00`);
  date.setDate(date.getDate() + (8 - date.getDay()) % 7);
  return {
    iso: `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`,
    us: `${date.getMonth() + 1}/${date.getDate()}/${date.getFullYear()}`,
    date
  };
}

test('@smoke a single dated Monday shows one visit and retains it through revision', async ({ page }) => {
  const window = await configure(page);
  const monday = nextPlanningMonday(window.iso);
  const sentence = `i am going 1 day ${monday.us}, monday. all day. only`;
  await page.getByLabel('Number of weeks', { exact: true }).fill('');
  await page.getByLabel('Describe your visits', { exact: false }).fill(sentence);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(monday.iso);
  await expect(page.getByLabel('One visit', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toBeHidden();
  await expect(page.locator('#repeat-weekdays')).toBeHidden();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('1 visit. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(monday.iso);
  await page.getByRole('link', { name: 'Change the plan', exact: true }).click();
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(sentence);
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(monday.iso);
  await expect(page.getByLabel('One visit', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toBeHidden();
});

test('one day per week stays a three-week recurrence with its explicit start anchor', async ({ page }) => {
  const window = await configure(page);
  const monday = nextPlanningMonday(window.iso);
  await page.getByLabel('Describe your visits', { exact: false }).fill(
    `One day per week on Monday for three weeks starting ${monday.us}`);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByLabel('Start of your planning period')).toHaveValue(monday.iso);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('3');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('3 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$3.00');
});

test('a conflicting dated one-off cannot silently compare a different Monday', async ({ page }) => {
  const window = await configure(page);
  const monday = nextPlanningMonday(window.iso);
  const tuesday = new Date(monday.date);
  tuesday.setDate(tuesday.getDate() + 1);
  const contradictoryDate = `${tuesday.getMonth() + 1}/${tuesday.getDate()}/${tuesday.getFullYear()}`;
  await page.getByLabel('Describe your visits', { exact: false }).fill(`One day Monday ${contradictoryDate} only for 1 week`);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('');
  await expect(page.locator('input[name="Weekdays"]:checked')).toHaveCount(0);
  await expect(page.getByTestId('schedule-reader')).toContainText('Check the visit date');
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
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

test('@smoke blank optional description permits reading and manual comparison', async ({ page }) => {
  await configure(page);
  await page.getByLabel('Describe your visits', { exact: false }).fill('   ');
  await page.getByLabel('Number of weeks', { exact: true }).fill('4');
  await page.getByLabel('Tuesday', { exact: true }).uncheck();
  await page.getByLabel('Wednesday', { exact: true }).check();
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'manual');
  await expect(page.getByTestId('schedule-reader')).toContainText('No description is needed');
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('4');
  await expect(page.getByLabel('Wednesday', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Tuesday', { exact: true })).not.toBeChecked();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('8 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$8.00');
});

test('returning from permit questions restores the full confirmed planner draft', async ({ page }) => {
  const { iso } = await configure(page);
  await page.getByLabel('Describe your visits', { exact: false }).fill('Tuesdays and Thursdays for four weeks');
  await page.getByLabel('Number of weeks', { exact: true }).fill('4');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Change the plan', exact: true }).click();
  await expect(page.getByLabel('Your college', { exact: true })).toHaveValue('Fresno City College');
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue('Tuesdays and Thursdays for four weeks');
  await expect(page.getByLabel('Start of your planning period')).toHaveValue(iso);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('4');
  await expect(page.getByLabel('Tuesday', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Thursday', { exact: true })).toBeChecked();
});

test('browser Back preserves unsent planner edits and invalidates the old comparison', async ({ page }) => {
  await configure(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await page.getByLabel('Number of weeks', { exact: true }).fill('5');
  await page.getByLabel('Describe your visits', { exact: false }).fill('My revised fictional visits');
  await page.getByLabel('Monday', { exact: true }).check();
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await expect(page.getByRole('status')).toContainText('Your schedule changed');
  await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
  await page.goBack();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('5');
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue('My revised fictional visits');
  await expect(page.getByLabel('Monday', { exact: true })).toBeChecked();
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('15 visits. Two ways to plan.');
});

test('without JavaScript blank reading is optional and changed comparisons require confirmation', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL });
  try {
    const page = await context.newPage();
    await configure(page);
    await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByTestId('schedule-reader')).toContainText('No description is needed');
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await page.getByLabel('Number of weeks', { exact: true }).fill('5');
    await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Your plan changed');
    await expect(page.getByTestId('plan-result')).toContainText('10 visits. Two ways to plan.');
    await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('5');
    await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Change the plan', exact: true }).click();
    await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('5');
  } finally { await context.close(); }
});
