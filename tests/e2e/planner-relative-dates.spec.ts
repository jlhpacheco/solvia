import { test, expect, type Page } from '@playwright/test';

// Includes context startup, navigation, reading, all seven fields and comparison.
// Per-assertion deadlines remain in the shared configuration.
test.describe.configure({ timeout: 90_000 });

const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const dayMs = 86_400_000;
const reported = 'I want a parking permit today and tomorrow as well on wednesday. please. this is going to be a big week for me';
const iso = (date: Date) => date.toISOString().slice(0, 10);
const plusDays = (date: Date, count: number) => new Date(date.getTime() + count * dayMs);

function pacificToday() {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(new Date());
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find(value => value.type === type)!.value);
  // UTC is only a representation of a Pacific calendar date. Adding one UTC
  // calendar day here cannot skip/duplicate a date across a Pacific DST change.
  return new Date(Date.UTC(part('year'), part('month') - 1, part('day')));
}

function relativePlan(today: Date, namedDay: number) {
  const namedDate = plusDays(today, (namedDay - today.getUTCDay() + 7) % 7);
  const dates = [...new Set([iso(today), iso(plusDays(today, 1)), iso(namedDate)])].sort();
  const selected = new Set(dates.map(value => weekdays[new Date(`${value}T00:00:00Z`).getUTCDay()]));
  const year = today.getUTCFullYear();
  const month = today.getUTCMonth() + 1;
  const term = month <= 5 ? 'Spring' : month <= 7 ? 'Summer' : 'Fall';
  const start = new Date(Date.UTC(year, term === 'Spring' ? 0 : term === 'Summer' ? 5 : 7,
    term === 'Spring' ? 20 : term === 'Summer' ? 8 : 24));
  const academicEnd = new Date(Date.UTC(year, term === 'Spring' ? 4 : term === 'Summer' ? 6 : 11,
    term === 'Spring' ? 15 : term === 'Summer' ? 31 : 11));
  // Existing demo term authority: first visit lies in the academic window;
  // the final visit cannot exceed its anchored 120-day permit end.
  const firstInTerm = today >= start && today <= academicEnd;
  const lastInPermit = dates.at(-1)! <= iso(plusDays(start, 120));
  return { today, dates, selected, term, firstInTerm, lastInPermit };
}

type RelativePlan = ReturnType<typeof relativePlan>;

async function guardRequests(page: Page) {
  let reads = 0;
  let comparisons = 0;
  const blocked: string[] = [];
  // The main/prebuilt controlled server pins hosted keys blank and the real
  // reader offline. We do not mock the relative-date reader or MVC response.
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
    const financial = /^\/(?:checkout|prepaid)(?:\/|$)/.test(path) || /^\/api\/(?:chat|paypal|prepaid)(?:\/|$)/.test(path);
    const provider = /(?:^|\.)(?:together\.ai|paypal\.com|paypalobjects\.com)$/.test(url.hostname) || (local && url.port === '11434');
    if (mutation || financial || provider) {
      blocked.push(`${request.method()} ${url.pathname}`);
      await route.abort();
      return;
    }
    await route.continue();
  });
  return {
    assert() {
      expect(reads).toBe(1);
      expect(comparisons).toBe(1);
      expect(blocked).toEqual([]);
    }
  };
}

async function open(page: Page, plan: RelativePlan) {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel(plan.term, { exact: true }).check();
}

async function readAndCheck(page: Page, sentence: string, plan: RelativePlan) {
  await page.getByLabel('Describe your visits', { exact: false }).fill(sentence);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expect(page.getByTestId('schedule-reader')).not.toContainText('needs your review');
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(sentence);
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(plan.dates[0]);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(plan.dates.at(-1)!);
  for (const day of weekdays) {
    const field = page.getByLabel(day, { exact: true });
    if (plan.selected.has(day)) await expect(field).toBeChecked();
    else await expect(field).not.toBeChecked();
  }
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
}

async function compareExactDates(page: Page, plan: RelativePlan) {
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  if (!plan.firstInTerm || !plan.lastInPermit) {
    // Relative words cannot move a person's dates into an academic window.
    // Outside those windows, preserve the parsed dates and assert the safe
    // rejection rather than claiming an unavailable price or skipping a test.
    const reason = !plan.firstInTerm ? "first visit must be inside the selected term's academic dates" : "crosses the selected term permit's end date";
    await expect(page.locator('#planner-errors')).toContainText(reason);
    await expect(page.getByTestId('plan-result')).toHaveCount(0);
    await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(plan.dates[0]);
    await expect(page.getByLabel('End date', { exact: true })).toHaveValue(plan.dates.at(-1)!);
    return;
  }
  await expect(page.getByTestId('plan-result')).toContainText(`${plan.dates.length} visits. Two ways to plan.`);
  await expect(page.getByTestId('daily-total')).toHaveText(`$${plan.dates.length}.00`);
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await expect(page.locator('#planner-errors')).toHaveCount(0);
  await page.getByText('Visit dates and price source', { exact: true }).click();
  const format = new Intl.DateTimeFormat('en-US', {
    weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC'
  });
  await expect(page.locator('.sv-visit-dates li')).toHaveText(plan.dates.map(date => format.format(new Date(`${date}T00:00:00Z`))));
}

test('@smoke today tomorrow and the upcoming Wednesday preserve each actual Pacific date exactly once', async ({ page }) => {
  const plan = relativePlan(pacificToday(), 3);
  const requests = await guardRequests(page);
  await open(page, plan);
  await readAndCheck(page, reported, plan);
  // Wednesday may already be today or tomorrow. It must not buy a duplicate.
  expect(plan.dates.length).toBe(plan.today.getUTCDay() === 2 || plan.today.getUTCDay() === 3 ? 2 : 3);
  await compareExactDates(page, plan);
  requests.assert();
});

test('a new relative-date reading replaces an unrelated old date range and weekday selection', async ({ page }) => {
  const today = pacificToday();
  const namedDay = (today.getUTCDay() + 3) % 7;
  const plan = relativePlan(today, namedDay);
  expect(plan.dates).toEqual([iso(today), iso(plusDays(today, 1)), iso(plusDays(today, 3))]);
  const requests = await guardRequests(page);
  await open(page, plan);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(iso(plusDays(today, 20)));
  await page.getByLabel('End date', { exact: true }).fill(iso(plusDays(today, 47)));
  for (const day of weekdays) await page.getByLabel(day, { exact: true }).setChecked(!plan.selected.has(day));
  const sentence = `I need parking today and tomorrow as well on ${weekdays[namedDay]}.`;
  await readAndCheck(page, sentence, plan);
  await compareExactDates(page, plan);
  requests.assert();
});

test('without JavaScript today tomorrow and a named upcoming weekday produce the same three exact dates', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const today = pacificToday();
    const namedDay = (today.getUTCDay() + 3) % 7;
    const plan = relativePlan(today, namedDay);
    expect(plan.dates).toHaveLength(3);
    const requests = await guardRequests(page);
    await open(page, plan);
    await readAndCheck(page, `Today and tomorrow, as well as ${weekdays[namedDay]}, I need parking.`, plan);
    await compareExactDates(page, plan);
    requests.assert();
  } finally {
    await context.close();
  }
});
