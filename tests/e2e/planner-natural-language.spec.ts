import { test, expect, type Page } from '@playwright/test';

test.describe.configure({ timeout: 60_000 });

const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const selectedDays = new Set(['Monday', 'Wednesday', 'Friday']);
const dayMs = 86_400_000;

function reportedRange() {
  // Keep the exact reported 2026 sentence while it can still be compared.
  // Afterwards use the next Oct26 Monday, preserving both named endpoint
  // weekdays, the three-visit short range and the 16-visit month-end range.
  const today = new Date();
  const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  let year = 2026;
  while (`${year}-10-26` < todayIso || new Date(Date.UTC(year, 9, 26)).getUTCDay() !== 1) year++;
  return {
    year, start: `${year}-10-26`, shortEnd: `${year}-10-30`, monthEnd: `${year}-11-30`,
    sentence: `10/26/${year} , monday to 10/30/${year}, Friday. for 3 visits, as well as Wednesday`,
    typoSentence: `Monay, Wendesday and Firday from 10/26/${year} until the end of Novemeber ${year}`,
    conflictingSentence: `10/26/${year} , monday to 10/30/${year}, Friday. for 4 visits, as well as Wednesday`
  };
}

async function guardRequests(page: Page) {
  let reads = 0;
  let comparisons = 0;
  const blocked: string[] = [];
  // Main/parent-controlled servers pin the actual reader to offline settings.
  // This does not mock /Planner/Read: the real spelling/grounding/MVC path runs.
  // Browser-side guards additionally prevent any checkout, vehicle inference,
  // application mutation or external payment/model request in these journeys.
  await page.route('**/*', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const local = ['localhost', '127.0.0.1'].includes(url.hostname);
    const path = url.pathname.toLowerCase();
    if (local && request.method() === 'POST' && path === '/planner/read') {
      reads++;
      await route.continue();
      return;
    }
    if (local && request.method() === 'POST' && path === '/planner/compare') {
      comparisons++;
      await route.continue();
      return;
    }
    const mutation = !['GET', 'HEAD', 'OPTIONS'].includes(request.method());
    const financialPage = /^\/(?:checkout|prepaid)(?:\/|$)/.test(path) ||
      /^\/api\/(?:paypal|prepaid|chat)(?:\/|$)/.test(path);
    const provider = /(?:^|\.)(?:paypal\.com|paypalobjects\.com|together\.ai)$/.test(url.hostname) ||
      (local && url.port === '11434');
    if (mutation || financialPage || provider) {
      blocked.push(`${request.method()} ${url.pathname}`);
      await route.abort();
      return;
    }
    await route.continue();
  });
  return {
    assert(readCount: number, comparisonCount: number) {
      expect(reads).toBe(readCount);
      expect(comparisons).toBe(comparisonCount);
      expect(blocked).toEqual([]);
    }
  };
}

async function open(page: Page) {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Fall', { exact: true }).check();
}

async function read(page: Page, sentence: string) {
  await page.getByLabel('Describe your visits', { exact: false }).fill(sentence);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(sentence);
}

async function expectConfirmedFields(page: Page, start: string, end: string) {
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(start);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(end);
  for (const day of weekdays) {
    const checkbox = page.getByLabel(day, { exact: true });
    if (selectedDays.has(day)) await expect(checkbox).toBeChecked();
    else await expect(checkbox).not.toBeChecked();
  }
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveCount(0);
}

function expectedDateLabels(start: string, end: string) {
  const format = new Intl.DateTimeFormat('en-US', {
    weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC'
  });
  const dates: string[] = [];
  for (let date = new Date(`${start}T00:00:00Z`); date <= new Date(`${end}T00:00:00Z`); date = new Date(date.getTime() + dayMs)) {
    if ([1, 3, 5].includes(date.getUTCDay())) dates.push(format.format(date));
  }
  return dates;
}

async function compare(page: Page, start: string, end: string, count: number) {
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText(`${count} visits. Two ways to plan.`);
  await expect(page.getByTestId('daily-total')).toHaveText(`$${count}.00`);
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await expect(page.locator('#planner-errors')).toHaveCount(0);
  await page.getByText('Visit dates and price source', { exact: true }).click();
  const expected = expectedDateLabels(start, end);
  expect(expected).toHaveLength(count);
  await expect(page.locator('.sv-visit-dates li')).toHaveText(expected);
}

test('@smoke the reported interleaved endpoint sentence preserves exactly Monday Wednesday and Friday as three visits', async ({ page }) => {
  const range = reportedRange();
  const requests = await guardRequests(page);
  await open(page);
  await read(page, range.sentence);
  await expectConfirmedFields(page, range.start, range.shortEnd);
  await compare(page, range.start, range.shortEnd, 3);
  requests.assert(1, 1);
});

test('common weekday and month spelling mistakes retain the grounded November endpoint and exactly sixteen visits', async ({ page }) => {
  const range = reportedRange();
  const requests = await guardRequests(page);
  await open(page);
  await read(page, range.typoSentence);
  await expectConfirmedFields(page, range.start, range.monthEnd);
  await compare(page, range.start, range.monthEnd, 16);
  requests.assert(1, 1);
});

test('a conflicting explicit visit count clears old suggestions and reports that review is needed', async ({ page }) => {
  const range = reportedRange();
  const requests = await guardRequests(page);
  await open(page);
  // A failed reading must not inherit this previously entered end or weekdays.
  const oldStart = `${range.year + 1}-01-20`;
  await page.getByLabel('Start of your planning period', { exact: true }).fill(oldStart);
  await page.getByLabel('End date', { exact: true }).fill(`${range.year + 1}-02-16`);
  for (const day of weekdays) {
    await page.getByLabel(day, { exact: true }).setChecked(['Tuesday', 'Thursday'].includes(day));
  }
  await read(page, range.conflictingSentence);
  await expect(page.getByTestId('schedule-reader')).toContainText('needs your review');
  await expect(page.getByTestId('schedule-reader')).not.toContainText('Read by');
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(oldStart);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue('');
  await expect(page.locator('input[name="Weekdays"]:checked')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  // The native required end prevents accidental comparison of the old draft.
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.locator('#EndDate:invalid')).toHaveCount(1);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  requests.assert(1, 0);
});

test('without JavaScript the same natural-language sentence survives reading and produces the exact three-date comparison', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const range = reportedRange();
    const requests = await guardRequests(page);
    await open(page);
    await read(page, range.sentence);
    await expectConfirmedFields(page, range.start, range.shortEnd);
    await compare(page, range.start, range.shortEnd, 3);
    requests.assert(1, 1);
  } finally {
    await context.close();
  }
});
