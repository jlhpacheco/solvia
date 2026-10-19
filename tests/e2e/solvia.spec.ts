import { test, expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const gridBundle = path.resolve(__dirname, '../../node_modules/ag-grid-community/dist/ag-grid-community.min.js');
const gridUrl = '**/ag-grid-community@34.3.1/dist/ag-grid-community.min.js';
let browserErrors: string[];

test.beforeEach(async ({ page }) => {
  browserErrors = [];
  page.on('pageerror', error => browserErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && message.text().includes('AG Grid:')) browserErrors.push(message.text());
  });
  // Deliver the real pinned sponsor library locally so CDN availability cannot
  // mask UI regressions. No application responses or payment outcomes are mocked.
  await page.route(gridUrl, route => route.fulfill({ path: gridBundle, contentType: 'text/javascript' }));
});

test.afterEach(() => expect(browserErrors, 'Uncaught browser or AG Grid errors').toEqual([]));

const smokeRoutes = [
  '/', '/Permits/Ask', '/Permits/Apply', '/Permits/My', '/Vehicles',
  '/College/Fees', '/College/Dates', '/College/Roster', '/College/Search?q=Fresno', '/Home/Privacy'
];

for (const route of smokeRoutes) {
  test(`@smoke page loads: ${route}`, async ({ page }) => {
    const response = await page.goto(route);
    expect(response?.status()).toBe(200);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('.sv-header')).toHaveCSS('display', 'block');
    expect(await page.locator('body').evaluate(element => getComputedStyle(element).fontFamily)).not.toContain('Times New Roman');
    await expect(page.locator('body')).not.toContainText('An unhandled exception occurred');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'No page-wide horizontal overflow').toBe(true);
  });
}

test('@smoke local styles and scripts contain their actual bytes', async ({ request }) => {
  for (const asset of ['/css/site.css', '/js/feetable.js', '/js/feetable.aggrid.js']) {
    const response = await request.get(asset);
    expect(response.status()).toBe(200);
    expect((await response.body()).length, asset).toBeGreaterThan(100);
  }
});

test('@smoke workspace links navigate in the current tab', async ({ page }) => {
  await page.goto('/College/Fees');
  const workspace = page.getByRole('navigation', { name: 'Permit workspace' });
  await workspace.getByRole('link', { name: 'Ask Solvia' }).click();
  await expect(page).toHaveURL(/\/Permits\/Ask$/);
  await page.getByRole('link', { name: 'Fresno City College $30.00 per term', exact: true }).click();
  await expect(page).toHaveURL(/\/Permits\/Questions\?college=/);
  await expect(page.getByRole('heading', { name: 'Three quick questions' })).toBeVisible();
});

test('@smoke sponsor grid searches and sorts actual fees', async ({ page }, info) => {
  await page.goto('/College/Fees');
  await expect(page.locator('#feeGrid .ag-root-wrapper')).toBeVisible();
  await page.getByLabel('Find a campus or fee').fill('Fresno');
  await expect(page.getByRole('status')).toHaveText('1 of 10 campuses');
  await expect(page.locator('#feeGrid .ag-center-cols-container .ag-row')).toHaveCount(1);
  await expect(page.locator('#feeGrid .ag-center-cols-container')).toContainText('Fresno City College');
  await page.getByLabel('Find a campus or fee').fill('');
  await expect(page.getByRole('status')).toHaveText('10 of 10 campuses');
  await page.getByRole('columnheader', { name: 'Semester price' }).click();
  // AG Grid recycles DOM nodes; row-index, not DOM insertion order, is the
  // displayed order after sorting a previously filtered grid.
  await expect(page.locator('#feeGrid .ag-row[row-index="0"] [col-id="semesterPrice"]')).toHaveText('$0 FREE');
  await page.screenshot({ path: info.outputPath('fees.png'), fullPage: true });
});

test('fee search survives a blocked CDN', async ({ page }) => {
  await page.route(gridUrl, route => route.abort());
  await page.goto('/College/Fees');
  await expect(page.locator('#feeGrid table')).toBeVisible();
  await page.getByLabel('Find a campus or fee').fill('Fresno');
  await expect(page.getByRole('status')).toHaveText('1 of 10 campuses');
  await expect(page.locator('#feeGrid tbody tr:visible')).toHaveCount(1);
});

async function describeCar(page: Page, sentence: string, daily = false) {
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  await page.getByLabel('2 · Your car', { exact: false }).fill(sentence);
  if (daily) await page.locator('label[for="typeDaily"]').click();
  await page.getByRole('button', { name: 'Read my answers' }).click();
  await expect(page.getByRole('heading', { name: 'Did I get this right?' })).toBeVisible();
}

test('@smoke numeric license plate survives the Volvo sentence', async ({ page }, info) => {
  await describeCar(page, 'I have a volvo 760 white color and license plate 999111');
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue('999111');
  await expect(page.getByLabel('Make', { exact: true })).toHaveValue('Volvo');
  await expect(page.getByLabel('Model', { exact: true })).toHaveValue('760');
  await expect(page.getByLabel('Color', { exact: true })).toHaveValue('White');
  await expect(page.getByRole('button', { name: 'Yes — show my total' })).toBeEnabled();
  await page.screenshot({ path: info.outputPath('plate-confirmation.png'), fullPage: true });
});

test('compound capitalized plate labels are read', async ({ page }) => {
  await describeCar(page, 'blue Honda Civic, LICENSE PLATE NUMBER: ABC-123');
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue('ABC123');
});

test('missing plate asks for correction and blocks continuation', async ({ page }) => {
  await describeCar(page, 'white Volvo 760');
  await expect(page.getByRole('alert').filter({ hasText: 'I still need your plate' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Yes — show my total' })).toBeDisabled();
  await page.getByLabel('License plate', { exact: true }).fill('AB1234');
  await expect(page.getByRole('button', { name: 'Yes — show my total' })).toBeEnabled();
});

for (const person of ['Maria', 'Pat']) {
  for (const daily of [false, true]) {
    test(`${person} can choose and review a ${daily ? 'day pass' : 'term permit'}`, async ({ page }) => {
      await page.goto('/');
      await page.getByRole('button', { name: person, exact: true }).click();
      const plate = 'T' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
      await describeCar(page, `blue Honda Civic, license plate ${plate}`, daily);
      await page.getByRole('button', { name: 'Yes — show my total' }).click();
      await expect(page.getByRole('heading', { name: 'Check your total' })).toBeVisible();
      await expect(page.locator('main')).toContainText(daily ? '$1.00' : '$30.00');
      await expect(page.locator('main')).toContainText(plate);
      await page.getByRole('button', { name: 'Pay with PayPal', exact: true }).click();
      await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
      await expect(page.locator('#checkout-payment')).toContainText(`$${daily ? '1.00' : '30.00'}`);
      await expect(page.getByRole('button', { name: /^Pay \$/ })).toHaveCount(0);
      // No credentials are configured in this isolated server. Separate opt-in
      // tests verify Sandbox order/capture; this journey does not approve a payment.
      await expect(page.getByText('PayPal checkout is unavailable right now.', { exact: false })).toBeVisible();
    });
  }
}

test('employee permit issues without payment and downloads a real PDF', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Elena', exact: true }).click();
  const plate = 'E' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await describeCar(page, `white Volvo 760, license plate ${plate}`);
  await page.getByRole('button', { name: 'Yes — show my total' }).click();
  await expect(page.getByRole('heading', { name: /Permit live/ })).toBeVisible();
  await expect(page.locator('main')).toContainText('EXEMPT (Employee)');
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('link', { name: 'Download permit (PDF)' }).click();
  const download = await downloadEvent;
  const file = await download.path();
  expect(file).toBeTruthy();
  const bytes = readFileSync(file!);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  expect(bytes.length).toBeGreaterThan(1000);
});

test('payment endpoints reject missing antiforgery tokens', async ({ request }) => {
  const response = await request.post('/api/paypal/orders', {
    data: { permitApplicationId: '00000000-0000-0000-0000-000000000001' }
  });
  expect(response.status()).toBe(400);
});
