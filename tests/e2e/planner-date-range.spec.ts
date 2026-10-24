import { test, expect, type Page } from '@playwright/test';

test.describe.configure({ timeout: 60_000 });

const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const visitWeekdays = new Set([1, 3, 5]);
const calendarDay = 86_400_000;

function iso(date: Date) { return date.toISOString().slice(0, 10); }

// Derive a future, term-safe range independently of the app's week rounding.
// Its final date is the 26th selected weekday, not the end of nine full weeks.
function springRange() {
  const year = new Date().getFullYear() + 1;
  const start = new Date(Date.UTC(year, 0, 20));
  const visits: Date[] = [];
  for (let date = new Date(start); visits.length < 26; date = new Date(date.getTime() + calendarDay)) {
    if (visitWeekdays.has(date.getUTCDay())) visits.push(date);
  }
  return { start: iso(start), end: iso(visits.at(-1)!), visits };
}

function plusDays(value: string, days: number) {
  return iso(new Date(new Date(`${value}T00:00:00Z`).getTime() + days * calendarDay));
}

function displayedDates(dates: Date[]) {
  return dates.map(date => new Intl.DateTimeFormat('en-US', {
    weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC'
  }).format(date));
}

async function guardReadingAndPayments(page: Page, allowScheduleRead = false) {
  const rejected: string[] = [];
  let reads = 0;
  // The main Playwright config pins the real server reader to offline Ollama
  // configuration (port 1, blank hosted credentials). Do not replace the reader
  // response: /Planner/Read must exercise its actual deterministic grounding.
  await page.route(/\/Planner\/Read(?:[/?]|$)|\/Permits\/(?:Understand|Review|Confirm)(?:[/?]|$)|\/api\/(?:chat|paypal)(?:[/?]|$)|\/Checkout(?:[/?]|$)|https?:\/\/(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)(?:[/:]|$)|https?:\/\/(?:localhost|127\.0\.0\.1):11434(?:[/?]|$)/i, async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (allowScheduleRead && url.pathname.toLowerCase() === '/planner/read' && request.method() === 'POST') {
      reads++;
      await route.continue();
      return;
    }
    rejected.push(`${request.method()} ${url.pathname}`);
    await route.abort();
  });
  return { rejected, reads: () => reads };
}

async function openRepeating(page: Page, term = 'Spring') {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel(term, { exact: true }).check();
}

async function chooseMwf(page: Page) {
  for (const day of weekdays) {
    await page.getByLabel(day, { exact: true }).setChecked(['Monday', 'Wednesday', 'Friday'].includes(day));
  }
}

async function expectMwf(page: Page) {
  for (const day of weekdays) {
    const checkbox = page.getByLabel(day, { exact: true });
    if (['Monday', 'Wednesday', 'Friday'].includes(day)) await expect(checkbox).toBeChecked();
    else await expect(checkbox).not.toBeChecked();
  }
}

async function expectRange(page: Page, start: string, end: string) {
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(start);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(end);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveCount(0);
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.locator('#EndDate')).toBeEnabled();
}

async function expectTwentySixVisits(page: Page, visits: Date[]) {
  await expect(page.getByTestId('plan-result')).toContainText('26 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$26.00');
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByText('Visit dates and price source', { exact: true }).click();
  await expect(page.locator('.sv-visit-dates li')).toHaveText(displayedDates(visits));
}

async function restoreHistoryFixture(page: Page, end: string, legacyWeeks?: string) {
  // Compatibility/event emulation: the new UI cannot write old Weeks fields.
  // Seed only history data, then let the real pageshow restore handler update
  // the real form. The separate Back journey above uses actual navigation.
  await page.evaluate(({ end, legacyWeeks }) => {
    const form = document.querySelector<HTMLFormElement>('#planner-form')!;
    const names = new Set(['Campus', 'ScheduleText', 'SingleVisit', 'StartDate', 'EndDate', 'Weekdays', 'Term']);
    const fields = Array.from(form.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
      'input[name], select[name], textarea[name]'))
      .filter(input => names.has(input.name))
      .map(input => ({
        name: input.name,
        value: input.name === 'EndDate' ? end : input.value,
        checked: input instanceof HTMLInputElement && (input.type === 'checkbox' || input.type === 'radio')
          ? input.checked : null
      }));
    if (legacyWeeks !== undefined) fields.push({ name: 'Weeks', value: legacyWeeks, checked: null });
    history.replaceState({ ...history.state, solviaPlannerDraft: { owner: form.dataset.person, fields } }, '');
    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
  }, { end, legacyWeeks });
}

test('the exact stated date range reads MWF and both bounds, then rejects visits beyond the Fall permit window', async ({ page }) => {
  const requests = await guardReadingAndPayments(page, true);
  await openRepeating(page, 'Fall');
  await page.getByLabel('Describe your visits', { exact: false }).fill(
    'I am coming to campus on monday, wednesday, and Friday from 11/01/2026 to 12/31/2026');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expectRange(page, '2026-11-01', '2026-12-31');
  await expectMwf(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  // The demo Fall permit expires December 22. Parsing the sentence must not
  // imply that a term permit covers its December 23-31 visits.
  await expect(page.getByRole('alert')).toContainText("crosses the selected term permit's end date");
  await expect(page.locator('#planner-errors')).toBeFocused();
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  await expectRange(page, '2026-11-01', '2026-12-31');
  expect(requests.reads()).toBe(1);
  expect(requests.rejected).toEqual([]);
});

test('@smoke an exact range counts only 26 selected visits and retains its bounds through Change the plan', async ({ page }) => {
  // Two complete comparisons, handoff and revision are one long journey.
  // Preserve the normal assertion deadlines on slower WebKit hosts.
  test.setTimeout(90_000);
  const requests = await guardReadingAndPayments(page);
  const range = springRange();
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await page.getByLabel('End date', { exact: true }).fill(range.end);
  await chooseMwf(page);
  await expectRange(page, range.start, range.end);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.locator('#comparison-title')).toBeFocused();
  await expectTwentySixVisits(page, range.visits);
  await page.getByRole('button', { name: 'Start with one day pass', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Three quick questions', exact: true })).toBeVisible();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(iso(range.visits[0]));
  await expect(page.getByLabel('Day pass — one day', { exact: true })).toBeChecked();
  await page.getByRole('link', { name: 'Change the plan', exact: true }).click();
  await expectRange(page, range.start, range.end);
  await expectMwf(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expectTwentySixVisits(page, range.visits);
  expect(requests.reads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});

test('editing the start and end dates retains exact bounds without a user-facing weeks field', async ({ page }) => {
  // Several native date edits plus an exact-date comparison share this budget.
  // Keep individual assertion deadlines unchanged on slower WebKit hosts.
  test.setTimeout(90_000);
  const requests = await guardReadingAndPayments(page);
  const { start, visits } = springRange();
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(start);
  await page.getByLabel('End date', { exact: true }).fill(plusDays(start, 13));
  await page.getByLabel('End date', { exact: true }).press('Tab');
  await expectRange(page, start, plusDays(start, 13));
  // A partial final week must not expand the person's exact chosen dates.
  await page.getByLabel('End date', { exact: true }).fill(plusDays(start, 14));
  await page.getByLabel('End date', { exact: true }).press('Tab');
  await expectRange(page, start, plusDays(start, 14));
  await page.getByLabel('Start of your planning period', { exact: true }).fill(plusDays(start, 1));
  await expectRange(page, plusDays(start, 1), plusDays(start, 14));
  await page.getByLabel('End date', { exact: true }).fill(plusDays(start, 28));
  await expectRange(page, plusDays(start, 1), plusDays(start, 28));
  await chooseMwf(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('12 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$12.00');
  await page.getByText('Visit dates and price source', { exact: true }).click();
  const expected = visits.filter(date => iso(date) >= plusDays(start, 1) && iso(date) <= plusDays(start, 28));
  await expect(page.locator('.sv-visit-dates li')).toHaveText(displayedDates(expected));
  expect(requests.reads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});

test('browser Back restores unsent range edits and requires a fresh comparison', async ({ page }) => {
  test.setTimeout(90_000);
  const requests = await guardReadingAndPayments(page);
  const range = springRange();
  const shortenedEnd = plusDays(range.start, 14);
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await page.getByLabel('End date', { exact: true }).fill(range.end);
  await chooseMwf(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toBeVisible();
  await page.getByLabel('End date', { exact: true }).fill(shortenedEnd);
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
  await page.goBack();
  await expectRange(page, range.start, shortenedEnd);
  await expectMwf(page);
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await expect(page.locator('#plan-edited')).toBeVisible();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  const expected = range.visits.filter(date => iso(date) <= shortenedEnd);
  await expect(page.getByTestId('plan-result')).toContainText(`${expected.length} visits. Two ways to plan.`);
  await expect(page.locator('#comparison-title')).toBeFocused();
  await page.getByText('Visit dates and price source', { exact: true }).click();
  await expect(page.locator('.sv-visit-dates li')).toHaveText(displayedDates(expected));
  expect(requests.rejected).toEqual([]);
});

test('one visit hides and disables an invalid repeating end date before comparison', async ({ page }) => {
  const requests = await guardReadingAndPayments(page);
  const { start } = springRange();
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(start);
  await page.getByLabel('End date', { exact: true }).fill(plusDays(start, -1));
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await expect(page.locator('#repeat-end')).toBeHidden();
  await expect(page.locator('#EndDate')).toBeDisabled();
  await expect(page.locator('#repeat-weeks')).toHaveCount(0);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveCount(0);
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.locator('#repeat-weekdays')).toBeHidden();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toContainText('1 visit. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await expect(page.locator('#EndDate')).toBeDisabled();
  await expect(page.locator('#EndDate')).toHaveValue('');
  expect(requests.rejected).toEqual([]);
});

test('without JavaScript exact dates alone produce the same bounded 26-visit comparison', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const requests = await guardReadingAndPayments(page);
    const range = springRange();
    await openRepeating(page);
    await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
    await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveCount(0);
    await expect(page.locator('#Weeks')).toHaveCount(0);
    await page.getByLabel('End date', { exact: true }).fill(range.end);
    await chooseMwf(page);
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await expectRange(page, range.start, range.end);
    await expectTwentySixVisits(page, range.visits);
    expect(requests.reads()).toBe(0);
    expect(requests.rejected).toEqual([]);
  } finally { await context.close(); }
});

test('a same-owner legacy history snapshot migrates four weeks and a blank end into exact dates', async ({ page }) => {
  const requests = await guardReadingAndPayments(page);
  const range = springRange();
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await chooseMwf(page);
  await restoreHistoryFixture(page, '', '4');
  const end = plusDays(range.start, 27);
  await expectRange(page, range.start, end);
  await expectMwf(page);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('plan-result')).toContainText('12 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$12.00');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByText('Visit dates and price source', { exact: true }).click();
  await expect(page.locator('.sv-visit-dates li')).toHaveText(displayedDates(range.visits.filter(date => iso(date) <= end)));
  expect(requests.reads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});

test('a new date-only history snapshot preserves a deliberately blank end instead of inventing a duration', async ({ page }) => {
  const requests = await guardReadingAndPayments(page);
  const range = springRange();
  let comparisons = 0;
  page.on('request', request => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === '/Planner/Compare') comparisons++;
  });
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await page.getByLabel('End date', { exact: true }).fill(range.end);
  await restoreHistoryFixture(page, '');
  await expectRange(page, range.start, '');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  expect(await page.locator('#EndDate').evaluate((element: HTMLInputElement) => element.validity.valueMissing)).toBe(true);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  expect(comparisons, 'The required end date must block comparison before a POST').toBe(0);
  expect(requests.reads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});

test('an invalid nonblank legacy history end is not silently replaced by its old four-week duration', async ({ page }) => {
  const requests = await guardReadingAndPayments(page);
  const range = springRange();
  let comparisons = 0;
  let comparisonStatus: number | undefined;
  page.on('request', request => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === '/Planner/Compare') comparisons++;
  });
  page.on('response', response => {
    if (response.request().method() === 'POST' && new URL(response.url()).pathname === '/Planner/Compare') {
      comparisonStatus = response.status();
    }
  });
  await openRepeating(page);
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await page.getByLabel('End date', { exact: true }).fill(range.end);
  // Chromium clears this invalid native date; WebKit can retain its raw text.
  // Neither behavior may trigger migration to a guessed four-week end date.
  const invalidEnd = `${new Date().getFullYear() + 1}-02-30`;
  await restoreHistoryFixture(page, invalidEnd, '4');
  const endInput = page.getByLabel('End date', { exact: true });
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(range.start);
  await expect(endInput).toBeEnabled();
  expect(['', invalidEnd]).toContain(await endInput.inputValue());
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  // Inspect actual native validation instead of assuming one engine's date
  // sanitization. If submission is permitted, the server must reject it.
  await expect.poll(async () => {
    if (comparisons > 0) return comparisonStatus === undefined ? 'pending' : 'server';
    return await endInput.evaluate((element: HTMLInputElement) => element.validity.valid) ? 'pending' : 'native';
  }, { message: 'The malformed date must be blocked natively or rejected by the server' }).not.toBe('pending');
  if (comparisons === 0) {
    expect(await endInput.evaluate((element: HTMLInputElement) => element.validity.valid)).toBe(false);
  } else {
    expect(comparisons, 'Only the explicit comparison may be posted').toBe(1);
    expect(comparisonStatus).toBe(200);
    await expect(page.getByRole('alert')).toContainText(/end\s*date|valid.*date/i);
  }
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  expect(['', invalidEnd], 'A malformed restored end must require a new explicit date').toContain(await endInput.inputValue());
  expect(requests.reads()).toBe(0);
  expect(requests.rejected).toEqual([]);
});
