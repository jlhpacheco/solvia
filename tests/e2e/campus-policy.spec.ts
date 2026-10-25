import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test.describe.configure({ timeout: 120_000 });

const mjc = 'Modesto Junior College / Columbia College / YCCD';
const campuses = [
  'CSU Monterey Bay', 'CSU East Bay', 'CSU Bakersfield', mjc,
  'Contra Costa College', 'Diablo Valley College', 'Los Medanos College',
  'Antelope Valley College', 'State Center Community College District', 'Fresno City College'
];
const weekdayNames = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function springDates() {
  const year = new Date().getFullYear() + 1;
  return { start: `${year}-01-20`, end: `${year}-02-09` };
}

async function guardMutations(page: Page) {
  const blocked: string[] = [];
  const updates: string[] = [];
  page.on('request', request => {
    const pathname = new URL(request.url()).pathname;
    if (request.method() === 'POST' && /^\/(?:Planner|Permits)\/UpdateCampus$/i.test(pathname)) updates.push(pathname);
  });
  // College information updates and manual comparisons are permitted. No AI
  // interpretation, permit submission, purchase creation, order or capture is.
  await page.route(/\/Planner\/Read(?:[/?]|$)|\/Permits\/(?:Understand|Review|Confirm)(?:[/?]|$)|\/api\/(?:chat|paypal|prepaid)(?:[/?]|$)|\/Checkout(?:[/?]|$)|\/Prepaid\/(?:Create|Start|Revise)(?:[/?]|$)|https?:\/\/(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)(?:[/:]|$)|https?:\/\/(?:localhost|127\.0\.0\.1):11434(?:[/?]|$)/i, async route => {
    blocked.push(`${route.request().method()} ${new URL(route.request().url()).pathname}`);
    await route.abort();
  });
  await page.route(/\/Permits\/Apply(?:[/?]|$)/i, async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    blocked.push('POST /Permits/Apply');
    await route.abort();
  });
  return { blocked, updates };
}

async function expectPolicy(page: Page, campus: string, label: 'Your college' | 'College') {
  const college = page.getByLabel(label, { exact: true });
  await expect(college).toHaveValue(campus);
  await expect(college).toHaveAttribute('aria-describedby', /\bselected-campus-policy\b/);
  const wrapper = page.locator('#selected-campus-policy[data-campus-policy]');
  await expect(wrapper).toHaveCount(1);
  const message = wrapper.locator('[data-campus-policy-message]');
  const note = message.getByTestId('mjc-policy-note');
  const rules = message.locator('a[href="https://mjc.edu/safety/parking.html"]');
  await expect(note).toHaveCount(1);
  if (campus === mjc) {
    await expect(message).toBeVisible();
    await expect(note).toContainText('not a requirement for exempt students');
    await expect(rules).toBeVisible();
    await expect(college).toHaveAccessibleDescription(/MJC policy checked.*students pay no parking fees/i);
  } else {
    await expect(message).toBeHidden();
    await expect(note).toBeHidden();
    await expect(rules).toBeHidden();
    await expect(college).not.toHaveAccessibleDescription(/MJC|students pay no parking fees|exempt students/i);
  }
}

async function confirmCatalog(page: Page, label: 'Your college' | 'College') {
  const actual = await page.getByLabel(label, { exact: true }).locator('option').evaluateAll(
    options => options.map(option => (option as HTMLOptionElement).value).filter(Boolean));
  expect(actual).toHaveLength(10);
  expect([...actual].sort()).toEqual([...campuses].sort());
}

async function addFictionalVehicle(page: Page) {
  const plate = 'CP' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.goto('/Vehicles/Create');
  await page.getByLabel('License plate', { exact: true }).fill(plate);
  await page.getByLabel('Make', { exact: true }).selectOption('Honda');
  await page.getByLabel('Model', { exact: true }).selectOption('Civic');
  await page.getByRole('button', { name: 'Save vehicle', exact: true }).click();
  await expect(page.locator('main')).toContainText(plate);
  return plate;
}

async function fillRepeatingDraft(page: Page) {
  const dates = springDates();
  const text = 'A fictional Monday, Wednesday and Friday schedule; confirm the dates manually.';
  await page.getByLabel('Start of your planning period', { exact: true }).fill(dates.start);
  await page.getByLabel('End date', { exact: true }).fill(dates.end);
  await page.getByLabel('Describe your visits', { exact: false }).fill(text);
  await page.getByLabel('Spring', { exact: true }).check();
  for (const day of weekdayNames)
    await page.getByLabel(day, { exact: true }).setChecked(['Monday', 'Wednesday', 'Friday'].includes(day));
  return { ...dates, text };
}

async function expectRepeatingDraft(page: Page, draft: Awaited<ReturnType<typeof fillRepeatingDraft>>) {
  await expect(page.getByRole('radio', { name: 'Repeating visits', exact: true })).toBeChecked();
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(draft.start);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(draft.end);
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(draft.text);
  await expect(page.getByLabel('Spring', { exact: true })).toBeChecked();
  for (const day of weekdayNames) {
    const input = page.getByLabel(day, { exact: true });
    if (['Monday', 'Wednesday', 'Friday'].includes(day)) await expect(input).toBeChecked();
    else await expect(input).not.toBeChecked();
  }
}

test('@smoke Planner shows MJC exemption information only for its own campus across all ten colleges', async ({ page }) => {
  test.setTimeout(180_000); // Ten native selections plus accessibility checks and browser startup.
  const requests = await guardMutations(page);
  await page.goto('/Planner');
  await confirmCatalog(page, 'Your college');
  for (const campus of campuses) {
    await page.getByLabel('Your college', { exact: true }).selectOption(campus);
    await expectPolicy(page, campus, 'Your college');
  }
  await expect(page.getByRole('button', { name: 'Update college information', exact: true })).toBeHidden();
  expect(requests.updates).toEqual([]);
  expect(requests.blocked).toEqual([]);
});

test('classic Apply updates its campus-specific note across all ten colleges without submitting an application', async ({ page }) => {
  test.setTimeout(180_000); // Ten native selections plus accessibility checks and vehicle setup.
  const requests = await guardMutations(page);
  await addFictionalVehicle(page);
  await page.goto('/Permits/Apply');
  await confirmCatalog(page, 'College');
  for (const campus of campuses) {
    await page.getByLabel('College', { exact: true }).selectOption(campus);
    await expectPolicy(page, campus, 'College');
  }
  await expect(page.getByRole('button', { name: 'Update college information', exact: true })).toBeHidden();
  await expect(page.locator('[data-update-campus]')).toHaveAttribute('type', 'button');
  await expect(page.getByRole('button', { name: 'Submit Application', exact: true })).toHaveAttribute('type', 'submit');
  expect(requests.updates).toEqual([]);
  expect(requests.blocked).toEqual([]);
});

test('college changes invalidate old Planner prices and Back restores the matching note and unsent inputs', async ({ page }) => {
  const requests = await guardMutations(page);
  const dates = springDates();
  const sentence = 'A fictional visit with a manually confirmed date.';
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'One visit', exact: true }).check();
  await page.getByLabel('Visit date', { exact: true }).fill(dates.start);
  await page.getByLabel('Describe your visits', { exact: false }).fill(sentence);
  await page.getByLabel('Spring', { exact: true }).check();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await expect(page.getByTestId('term-total')).toHaveText('$30.00');
  await page.getByLabel('Your college', { exact: true }).selectOption('Antelope Valley College');
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await expect(page.locator('#plan-edited')).toBeVisible();
  await expectPolicy(page, 'Antelope Valley College', 'Your college');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('daily-total')).toHaveText('$1.00');
  await expect(page.getByTestId('term-total')).toHaveText('$28.00');
  await page.getByLabel('Your college', { exact: true }).selectOption(mjc);
  await expectPolicy(page, mjc, 'Your college');
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('term-total')).toHaveText('$0.00');
  // The unsent college now differs from the last comparison. Back must not
  // show the free MJC term price as if it were Antelope Valley's price.
  await page.getByLabel('Your college', { exact: true }).selectOption('Antelope Valley College');
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await page.getByRole('link', { name: 'My permits', exact: true }).first().click();
  await page.goBack();
  await expectPolicy(page, 'Antelope Valley College', 'Your college');
  await expect(page.getByRole('radio', { name: 'One visit', exact: true })).toBeChecked();
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(dates.start);
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(sentence);
  await expect(page.getByLabel('Spring', { exact: true })).toBeChecked();
  await expect(page.getByTestId('plan-result')).toBeHidden();
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue(dates.start);
  await expect(page.getByTestId('plan-result')).toContainText('Antelope Valley College');
  await expect(page.getByTestId('term-total')).toHaveText('$28.00');
  expect(requests.updates).toEqual([]);
  expect(requests.blocked).toEqual([]);
});

test('without JavaScript updating college information preserves a repeating Planner draft without reading or purchasing', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const requests = await guardMutations(page);
    await page.goto('/Planner');
    const draft = await fillRepeatingDraft(page);
    for (const campus of [mjc, 'Antelope Valley College']) {
      await page.getByLabel('Your college', { exact: true }).selectOption(campus);
      await page.getByRole('button', { name: 'Update college information', exact: true }).click();
      await expectPolicy(page, campus, 'Your college');
      await expectRepeatingDraft(page, draft);
      await expect(page.getByTestId('plan-result')).toHaveCount(0);
    }
    expect(requests.updates).toEqual(['/Planner/UpdateCampus', '/Planner/UpdateCampus']);
    expect(requests.blocked).toEqual([]);
  } finally { await context.close(); }
});

test('without JavaScript classic college updates preserve vehicle, permit type, term and date without applying', async ({ page, browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const setup = await guardMutations(page);
  const plate = await addFictionalVehicle(page);
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    await context.addCookies(await page.context().cookies());
    const nativePage = await context.newPage();
    const requests = await guardMutations(nativePage);
    await nativePage.goto('/Permits/Apply');
    const vehicle = nativePage.getByLabel('Vehicle', { exact: true });
    const vehicleId = await vehicle.locator('option').filter({ hasText: plate }).getAttribute('value');
    expect(vehicleId).toMatch(/^[a-f0-9-]{36}$/i);
    await vehicle.selectOption(vehicleId!);
    const type = nativePage.getByLabel('Permit type', { exact: true });
    const typeId = await type.locator('option[data-code="Term"]').getAttribute('value');
    expect(typeId).toMatch(/^[1-9]\d*$/);
    await type.selectOption(typeId!);
    await nativePage.getByLabel('Term', { exact: true }).selectOption('Spring');
    const date = springDates().start;
    await nativePage.getByLabel('Start date', { exact: true }).fill(date);
    for (const campus of [mjc, 'Antelope Valley College']) {
      await nativePage.getByLabel('College', { exact: true }).selectOption(campus);
      await nativePage.getByRole('button', { name: 'Update college information', exact: true }).click();
      await expectPolicy(nativePage, campus, 'College');
      await expect(nativePage.getByLabel('Vehicle', { exact: true })).toHaveValue(vehicleId!);
      await expect(nativePage.getByLabel('Permit type', { exact: true })).toHaveValue(typeId!);
      await expect(nativePage.getByLabel('Term', { exact: true })).toHaveValue('Spring');
      await expect(nativePage.getByLabel('Start date', { exact: true })).toHaveValue(date);
      await expect(nativePage.getByRole('alert')).toHaveCount(0);
    }
    expect(requests.updates).toEqual(['/Permits/UpdateCampus', '/Permits/UpdateCampus']);
    expect(requests.blocked).toEqual([]);
    expect(setup.blocked).toEqual([]);
  } finally { await context.close(); }
});
