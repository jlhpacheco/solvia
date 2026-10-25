import { test, expect, type Page } from '@playwright/test';

test.describe.configure({ timeout: 90_000 });

const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const calendarDay = 86_400_000;

function monthEndSchedule() {
  const now = new Date();
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  let year = now.getFullYear();
  // Exercise the user's exact 2026 dates while they remain future dates.
  // Later runs choose the next matching calendar: October 26 must really be
  // Monday so the sentence and its independently counted 16 visits agree.
  while (Date.UTC(year, 9, 26) < today || new Date(Date.UTC(year, 9, 26)).getUTCDay() !== 1) year++;
  const start = `${year}-10-26`;
  const end = `${year}-11-30`;
  const visits: string[] = [];
  for (let time = Date.UTC(year, 9, 26); time <= Date.UTC(year, 10, 30); time += calendarDay) {
    const date = new Date(time);
    if ([1, 3, 5].includes(date.getUTCDay())) visits.push(date.toISOString().slice(0, 10));
  }
  return {
    start, end, visits,
    text: `i am coming over monday 10/26/${year} until the end of november ${year}, every monday , Wednesday and Friday. ok`
  };
}

async function offlineReadGuard(page: Page) {
  let reads = 0;
  const blocked: string[] = [];
  // The controlled server has hosted credentials blank and local inference
  // disabled. Its real /Planner/Read handler must supply the fallback result;
  // this test neither mocks extraction nor creates a purchase/payment.
  await page.route(/\/Planner\/Read(?:[/?]|$)|\/Permits\/(?:Understand|Review|Confirm)(?:[/?]|$)|\/api\/(?:chat|paypal|prepaid)(?:[/?]|$)|\/Checkout(?:[/?]|$)|\/Prepaid\/(?:Create|Start|Revise)(?:[/?]|$)|https?:\/\/(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)(?:[/:]|$)|https?:\/\/(?:localhost|127\.0\.0\.1):11434(?:[/?]|$)/i, async route => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (pathname.toLowerCase() === '/planner/read' && request.method() === 'POST') {
      reads++;
      await route.continue();
      return;
    }
    blocked.push(`${request.method()} ${pathname}`);
    await route.abort();
  });
  return { reads: () => reads, blocked };
}

async function readMonthEnd(page: Page, schedule: ReturnType<typeof monthEndSchedule>) {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Fall', { exact: true }).check();
  await page.getByLabel('End date', { exact: true }).fill('');
  await page.getByLabel('Describe your visits', { exact: false }).fill(schedule.text);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  await expect(page.getByTestId('schedule-reader')).toHaveAttribute('data-source', 'fallback');
  await expectSchedule(page, schedule);
}

async function expectSchedule(page: Page, schedule: ReturnType<typeof monthEndSchedule>) {
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(schedule.start);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(schedule.end);
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(schedule.text);
  for (const day of days) {
    const checkbox = page.getByLabel(day, { exact: true });
    if (['Monday', 'Wednesday', 'Friday'].includes(day)) await expect(checkbox).toBeChecked();
    else await expect(checkbox).not.toBeChecked();
  }
  await expect(page.locator('#Weeks')).toHaveCount(0);
}

async function compareSixteen(page: Page, schedule: ReturnType<typeof monthEndSchedule>) {
  expect(schedule.visits).toHaveLength(16);
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toContainText('16 visits. Two ways to plan.');
  await expect(page.getByTestId('daily-total')).toHaveText('$16.00');
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await page.getByText('Visit dates and price source', { exact: true }).click();
  const labels = schedule.visits.map(value => new Intl.DateTimeFormat('en-US', {
    weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC'
  }).format(new Date(`${value}T00:00:00Z`)));
  await expect(page.locator('.sv-visit-dates li')).toHaveText(labels);
}

test('@smoke a month-end sentence fills both dates and retains 16 MWF visits through Back and prepaid selection', async ({ page }) => {
  const schedule = monthEndSchedule();
  const requests = await offlineReadGuard(page);
  await readMonthEnd(page, schedule);
  await compareSixteen(page, schedule);
  await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
  await page.goBack();
  await expectSchedule(page, schedule);
  await compareSixteen(page, schedule);
  await page.getByRole('button', { name: 'Buy these dates', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Choose your prepaid dates', exact: true })).toBeVisible();
  const offered = page.getByTestId('prepaid-dates').locator('input[name="selectedDates"]');
  expect(await offered.evaluateAll(inputs => inputs.map(input => (input as HTMLInputElement).value))).toEqual(schedule.visits);
  await expect(page.getByTestId('prepaid-dates').locator('input[name="selectedDates"]:checked')).toHaveCount(16);
  expect(requests.reads()).toBe(1);
  expect(requests.blocked).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test('without JavaScript a month-end sentence supplies its exact end and survives a later planner GET', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const schedule = monthEndSchedule();
    const requests = await offlineReadGuard(page);
    await readMonthEnd(page, schedule);
    await compareSixteen(page, schedule);
    await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
    await page.goto('/Planner');
    await expectSchedule(page, schedule);
    expect(requests.reads()).toBe(1);
    expect(requests.blocked).toEqual([]);
  } finally { await context.close(); }
});
