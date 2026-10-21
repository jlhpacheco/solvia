import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

const campus = 'Fresno City College';
const plannedDate = `${new Date().getFullYear()}-11-03`;
const plate = () => 'R' + randomUUID().replaceAll('-', '').slice(0, 7).toUpperCase();

async function questions(page: Page, description: string) {
  await page.goto(`/Permits/Questions?college=${encodeURIComponent(campus)}&permitType=Daily&term=Summer&startDate=${plannedDate}`);
  await page.getByLabel(/Your car/).fill(description);
  await expect(page.locator('#typeDaily')).toBeChecked();
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Did I get this right?', exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/Permits\/Understood\?flowId=/);
}

async function correct(page: Page, license: string) {
  await page.getByLabel('License plate', { exact: true }).fill(license);
  await page.getByLabel('Plate state', { exact: true }).selectOption('NV');
  await page.getByLabel('Make', { exact: true }).fill('Volvo');
  await page.getByLabel('Model', { exact: true }).fill('740');
  await page.getByLabel('Color', { exact: true }).selectOption('White');
}

async function checkCorrected(page: Page, license: string) {
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue(license);
  await expect(page.getByLabel('Plate state', { exact: true })).toHaveValue('NV');
  await expect(page.getByLabel('Make', { exact: true })).toHaveValue('Volvo');
  await expect(page.getByLabel('Model', { exact: true })).toHaveValue('740');
  await expect(page.getByLabel('Color', { exact: true })).toHaveValue('White');
  await expect(page.locator('input[name="StartDate"]')).toHaveValue(plannedDate);
}

test('@smoke explicit permit revision keeps original sentence, corrected fields, choices, date and draft identity', async ({ page }) => {
  const license = plate();
  const sentence = `white Volvo 760, license plate ${license}`;
  await questions(page, sentence);
  await correct(page, license);
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  const reviewUrl = page.url();
  const id = await page.locator('form[action="/Permits/Questions"] input[name="ApplicationId"]').inputValue();
  await expect(page.locator('.display-4 strong')).toHaveText('$1.00');
  await page.getByRole('button', { name: 'Check my answers', exact: true }).click();
  await expect(page.getByLabel(/Your car/)).toHaveValue(sentence);
  await expect(page.locator('#typeDaily')).toBeChecked();
  await expect(page.locator('#termSummer')).toBeChecked();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(plannedDate);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await checkCorrected(page, license);
  await expect(page.locator('input[name="ApplicationId"]')).toHaveValue(id);
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page).toHaveURL(reviewUrl);
  await expect(page.locator('.display-4 strong')).toHaveText('$1.00');
  await expect(page.locator('main')).toContainText('740');
});

test('@smoke browser Back then Read keeps unsent vehicle corrections; changed sentence gets a fresh draft', async ({ page }) => {
  const license = plate();
  const sentence = `white Volvo 760, license plate ${license}`;
  await questions(page, sentence);
  await correct(page, license);
  await page.goBack();
  await expect(page.getByLabel(/Your car/)).toHaveValue(sentence);
  await expect(page.getByRole('button', { name: 'Read my answers', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await checkCorrected(page, license);
  await page.getByLabel('Model', { exact: true }).fill('750');
  await page.goBack();
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.getByLabel('Model', { exact: true })).toHaveValue('750');
  await page.goBack();
  const changedPlate = plate();
  await page.getByLabel(/Your car/).fill(`blue Honda Civic, license plate ${changedPlate}`);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.getByLabel('Model', { exact: true })).toHaveValue('Civic');
  await expect(page.getByLabel('Make', { exact: true })).toHaveValue('Honda');
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue(changedPlate);
});

test('no-JavaScript explicit revision retains the exact sentence, manual corrections and planned date', async ({ browser, baseURL }) => {
  // Include explicit context/trace cleanup in this two-round-trip journey's
  // budget on slower hosts; individual assertion timeouts stay unchanged.
  test.setTimeout(60_000);
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL });
  try {
    const page = await context.newPage();
    const license = plate();
    const sentence = `white Volvo 760, license plate ${license}`;
    await questions(page, sentence);
    await correct(page, license);
    await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
    await page.getByRole('button', { name: 'Check my answers', exact: true }).click();
    await expect(page.getByLabel(/Your car/)).toHaveValue(sentence);
    await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
    await checkCorrected(page, license);
  } finally { await context.close(); }
});

test('changing college preserves unsent vehicle corrections and planned choices', async ({ page }) => {
  const license = plate();
  const sentence = `white Volvo 760, license plate ${license}`;
  await questions(page, sentence);
  await correct(page, license);
  // Unsubmitted corrections must also survive Back -> change college.
  await page.goBack();
  await page.getByRole('button', { name: 'change', exact: true }).click();
  await page.getByRole('link', { name: /CSU East Bay/ }).click();
  await expect(page.getByLabel(/Your car/)).toHaveValue(sentence);
  await expect(page.locator('#typeDaily')).toBeChecked();
  await expect(page.locator('input[name="startDate"]')).toHaveValue(plannedDate);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await checkCorrected(page, license);
  await expect(page.locator('input[name="ApplicationId"]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('CSU East Bay');
});

test('a stale permit form cannot move its vehicle into another selected demo person', async ({ page, context }) => {
  await questions(page, `blue Honda Civic, license plate ${plate()}`);
  const other = await context.newPage();
  await other.goto('/');
  await other.getByRole('button', { name: 'Pat', exact: true }).click();
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pick your college', exact: true })).toBeVisible();
  await expect(page.locator('body')).toContainText('belongs to another demo person');
  await expect(page.locator('body')).toContainText('no payment was made');
});

test('a maximum-length Unicode description survives the protected-cookie roundtrip without header overflow', async ({ page, context }) => {
  const license = plate();
  const tail = ` white Volvo 760, license plate ${license}`;
  const sentence = Array.from({ length: 1000 - tail.length }, (_, i) => String.fromCharCode(0x4e00 + (i * 173) % 10000)).join('') + tail;
  await questions(page, sentence);
  await correct(page, license);
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await page.getByRole('button', { name: 'Check my answers', exact: true }).click();
  await expect(page.getByLabel(/Your car/)).toHaveValue(sentence);
  const cookies = await context.cookies();
  const header = cookies.map(cookie => `${cookie.name}=${cookie.value}`).join('; ');
  expect(Buffer.byteLength(header, 'utf8')).toBeLessThan(7000);
});
