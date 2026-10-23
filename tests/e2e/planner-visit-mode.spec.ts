import { test, expect, type Page } from '@playwright/test';

test.describe.configure({ timeout: 60_000 });

// A future Spring date avoids a dependence on today's weekday or UTC offset.
// Calendar labels are derived from local noon, never from a UTC-parsed date.
function visitDate(day = 20) {
  const year = new Date().getFullYear() + 1;
  const iso = `${year}-01-${String(day).padStart(2, '0')}`;
  const date = new Date(`${iso}T12:00:00`);
  return { iso, us: `1/${day}/${year}`, weekday: new Intl.DateTimeFormat('en-US', { weekday: 'long' }).format(date) };
}

async function guardReaderAndPayments(page: Page, allowScheduleRead = false) {
  const rejected: string[] = [];
  let scheduleReads = 0;
  // These journeys end before vehicle extraction or checkout. An unexpected
  // action is blocked rather than allowed to contact a model or payment SDK.
  await page.route(/\/Planner\/Read(?:[/?]|$)|\/Permits\/Understand(?:[/?]|$)|\/api\/(?:chat|paypal)(?:[/?]|$)|\/Checkout(?:[/?]|$)|https?:\/\/(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)(?:[/:]|$)|https?:\/\/(?:localhost|127\.0\.0\.1):11434(?:[/?]|$)/i, async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (allowScheduleRead && url.pathname.toLowerCase() === '/planner/read' && request.method() === 'POST') {
      scheduleReads++;
      await route.continue();
      return;
    }
    rejected.push(`${request.method()} ${url.pathname}`);
    await route.abort();
  });
  return { rejected, scheduleReads: () => scheduleReads };
}

async function openPlanner(page: Page) {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByLabel('Spring', { exact: true }).check();
}

async function expectSingleFields(page: Page, iso: string, liveSummary = true) {
  await expect(page.getByRole('radio', { name: 'One visit', exact: true })).toBeChecked();
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(iso);
  await expect(page.locator('#repeat-weeks')).toBeHidden();
  await expect(page.locator('#repeat-weekdays')).toBeHidden();
  await expect(page.locator('#Weeks')).toBeDisabled();
  await expect(page.locator('#day-Monday')).toBeDisabled();
  if (liveSummary) {
    await expect(page.locator('#single-visit-summary')).toBeVisible();
    await expect(page.locator('#single-visit-date')).toHaveAttribute('datetime', iso);
  } else {
    // No stale server-rendered date preview beside an edited native input.
    await expect(page.locator('#single-visit-summary')).toBeHidden();
  }
}

async function expectOneVisitComparison(page: Page) {
  await expect(page.getByTestId('plan-result')).toContainText('1 visit. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await expect(page.getByRole('alert')).toHaveCount(0);
}

test('@smoke one visit hides recurrence immediately and keeps its exact date through handoff and revision', async ({ page }) => {
  const requests = await guardReaderAndPayments(page);
  const first = visitDate();
  const revised = visitDate(21);
  await openPlanner(page);
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await page.getByLabel('Visit date', { exact: true }).fill(first.iso);
  await expectSingleFields(page, first.iso);
  await expect(page.getByRole('button', { name: 'Use this visit type', exact: true })).toBeHidden();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(first.iso);
  await expect(page.getByLabel('Day pass — one day', { exact: true })).toBeChecked();
  await page.getByRole('link', { name: 'Change the plan', exact: true }).click();
  await expectSingleFields(page, first.iso);
  await page.getByLabel('Visit date', { exact: true }).fill(revised.iso);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(revised.iso);
  expect(requests.scheduleReads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});

test('offline one-off reading selects One visit without exposing recurrence questions', async ({ page }) => {
  const requests = await guardReaderAndPayments(page, true);
  const visit = visitDate();
  await openPlanner(page);
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Start of your planning period', { exact: true }).fill(visitDate(21).iso);
  await page.getByLabel('Number of weeks', { exact: true }).fill('');
  await page.getByLabel('Describe your visits', { exact: false }).fill(`One visit on ${visit.us}.`);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expectSingleFields(page, visit.iso);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  expect(requests.scheduleReads()).toBe(1);
  expect(requests.rejected).toEqual([]);
});

test('switching a blank repeating schedule to one visit has no hidden required-field trap', async ({ page }) => {
  const requests = await guardReaderAndPayments(page);
  const visit = visitDate();
  await openPlanner(page);
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Number of weeks', { exact: true }).fill('');
  for (const day of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']) {
    await page.getByLabel(day, { exact: true }).uncheck();
  }
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await page.getByLabel('Visit date', { exact: true }).fill(visit.iso);
  await expectSingleFields(page, visit.iso);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  expect(requests.rejected).toEqual([]);
});

test('browser Back restores an unsent visit type and date with the matching field visibility', async ({ page }) => {
  const requests = await guardReaderAndPayments(page);
  const original = visitDate();
  const unsent = visitDate(21);
  await openPlanner(page);
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Start of your planning period', { exact: true }).fill(original.iso);
  await page.getByLabel('Number of weeks', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toBeVisible();
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await page.getByLabel('Visit date', { exact: true }).fill(unsent.iso);
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
  await page.goBack();
  await expectSingleFields(page, unsent.iso);
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await expect(page.locator('#plan-edited')).toBeVisible();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  expect(requests.rejected).toEqual([]);
});

test('without JavaScript visit-type switching bypasses hidden validation and repetition requires answers again', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const requests = await guardReaderAndPayments(page);
    const visit = visitDate();
    await openPlanner(page);
    await page.getByLabel('Number of weeks', { exact: true }).fill('');
    await page.getByRole('radio', { name: 'One visit', exact: true }).check();
    await page.getByRole('button', { name: 'Use this visit type', exact: true }).click();
    await page.getByLabel('Visit date', { exact: true }).fill(visit.iso);
    await expectSingleFields(page, visit.iso, false);
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await expectOneVisitComparison(page);
    await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
    await page.getByRole('button', { name: 'Use this visit type', exact: true }).click();
    await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(visit.iso);
    await expect(page.locator('#repeat-weeks')).toBeVisible();
    await expect(page.locator('#repeat-weekdays')).toBeVisible();
    await expect(page.getByLabel('Number of weeks', { exact: true })).toBeEnabled();
    await expect(page.getByLabel('Monday', { exact: true })).toBeEnabled();
    await page.getByLabel('Number of weeks', { exact: true }).fill('');
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    expect(await page.locator('#Weeks').evaluate((element: HTMLInputElement) => element.validity.valueMissing)).toBe(true);
    await expect(page.getByTestId('plan-result')).toHaveCount(0);
    await page.getByLabel('Number of weeks', { exact: true }).fill('2');
    for (const day of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']) {
      await page.getByLabel(day, { exact: true }).uncheck();
    }
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Choose at least one visit weekday');
    await page.getByLabel(visit.weekday, { exact: true }).check();
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await expect(page.getByTestId('plan-result')).toContainText('2 visits. Two ways to plan.');
    await expect(page.getByTestId('daily-total')).toHaveText('$2.00');
    expect(requests.scheduleReads()).toBe(0);
    expect(requests.rejected).toEqual([]);
  } finally { await context.close(); }
});

test('an uncertain new description cannot inherit an earlier one-visit confirmation', async ({ page }) => {
  const requests = await guardReaderAndPayments(page, true);
  const visit = visitDate();
  await openPlanner(page);
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await page.getByLabel('Visit date', { exact: true }).fill(visit.iso);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectOneVisitComparison(page);
  await page.getByLabel('Describe your visits', { exact: false }).fill('Maybe Monday or Thursday, not sure how many weeks');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('');
  await expect(page.locator('input[name="Weekdays"]:checked')).toHaveCount(0);
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expect(page.getByTestId('schedule-reader')).toContainText('Check this draft');
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  expect(requests.scheduleReads()).toBe(1);
  expect(requests.rejected).toEqual([]);
});
