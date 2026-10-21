import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

// Real local application records and the pinned Community bundle. These
// checks never approve a payment, call a provider, or fabricate paid rows.
test.describe.configure({ timeout: 60_000 });
const gridUrl = '**/ag-grid-community@34.3.1/dist/ag-grid-community.min.js';
const gridBundle = path.resolve(__dirname, '../../node_modules/ag-grid-community/dist/ag-grid-community.min.js');

test.beforeEach(async ({ page }) => {
  await page.route(gridUrl, route => route.fulfill({ path: gridBundle, contentType: 'text/javascript' }));
});

async function pendingApplication(page: Page) {
  const financialRequests: string[] = [];
  page.on('request', request => {
    if (/\/api\/paypal\//.test(request.url()) || /https:\/\/(?:www\.)?(?:sandbox\.)?paypal\.com\//.test(request.url()))
      financialRequests.push(request.url());
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Maria', exact: true }).click();
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  await page.getByLabel('2 · Your car', { exact: false }).fill(
    `Fictional blue Honda Civic, license plate R${randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase()}`);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Did I get this right?', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  const id = await page.locator('#checkout-payment').getAttribute('data-application-id');
  expect(id).toMatch(/^[a-f0-9-]{36}$/);
  return { id: id!, reference: 'Application ' + id!.replaceAll('-', '').slice(0, 8), financialRequests };
}

async function filteredReview(page: Page) {
  await page.goto('/Operations');
  await page.getByLabel('Campus', { exact: true }).selectOption('Fresno City College');
  await page.getByLabel('Payment status', { exact: true }).selectOption('Pending');
  await page.getByLabel('Category', { exact: true }).selectOption('Student');
  await page.getByLabel('Needs review only', { exact: true }).check();
  await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Payment review', exact: true })).toBeVisible();
  await expect(page.getByTestId('pr-paid-amount')).toHaveText('$0.00');
  await expect(page.getByTestId('pr-no-payment')).toHaveText('0');
  const count = Number(await page.getByTestId('pr-applications').textContent());
  expect(count).toBeGreaterThan(0);
  await expect(page.getByTestId('pr-attention-count')).toHaveText(String(count));
  await expect(page.getByTestId('pr-record-count')).toHaveText(`${count} records`);
  const rows = JSON.parse(await page.locator('#payment-review-data').textContent() || '[]');
  expect(rows).toHaveLength(count);
  expect(rows.every((row: any) => row.campus === 'Fresno City College' && row.paymentStatus === 'Pending' && row.category === 'Student')).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'No page-wide horizontal overflow').toBe(true);
}

test('@smoke payment review filters real pending records and opens saved-reference details', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const application = await pendingApplication(page);
  await filteredReview(page);
  await expect(page.locator('#payment-review-grid .ag-root-wrapper')).toBeVisible();
  await expect(page.locator('#payment-review-native')).toBeHidden();
  await expect(page.locator('#payment-review-grid .ag-cell[col-id="isActive"]').first()).toHaveText('Inactive');
  await expect(page.locator('#payment-review-grid .ag-cell[col-id="isActive"] input[type="checkbox"]')).toHaveCount(0);
  await page.getByRole('link', { name: application.reference, exact: true }).click();
  const details = page.locator('#record-' + application.id.replaceAll('-', ''));
  await expect(details).toHaveJSProperty('open', true);
  await expect(details).toContainText(application.id);
  await expect(details).toContainText('PayPal order ID');
  await expect(details).toContainText('PayPal capture ID');
  await expect(details).toContainText('Not recorded');
  await expect(details).toContainText('Awaiting payment');
  await expect(page.locator('.pr-page')).toContainText('PayPal was not contacted');
  expect(application.financialRequests).toEqual([]);
  expect(errors).toEqual([]);
});

test('payment review retains the native table and filters when the sponsor CDN is blocked', async ({ page }) => {
  const application = await pendingApplication(page);
  await page.route(gridUrl, route => route.abort('failed'));
  await filteredReview(page);
  await expect(page.locator('#payment-review-grid')).toBeHidden();
  await expect(page.locator('#payment-review-native')).toBeVisible();
  await expect(page.getByRole('table', { name: 'Saved Sandbox payment records for the selected filters' })).toBeVisible();
  await expect(page.locator('#payment-review-native')).toContainText(application.reference);
  expect(application.financialRequests).toEqual([]);
});

test('payment review keeps the original records when grid construction fails', async ({ page }) => {
  const application = await pendingApplication(page);
  await page.route(gridUrl, route => route.fulfill({ contentType: 'text/javascript', body:
    `window.agGrid = { themeQuartz: { withParams: () => ({}) }, createGrid: () => { throw new Error('Simulated render failure'); } };` }));
  await filteredReview(page);
  await expect(page.locator('#payment-review-grid')).toBeHidden();
  await expect(page.locator('#payment-review-native')).toBeVisible();
  await expect(page.locator('#payment-review-native')).toContainText(application.reference);
  expect(application.financialRequests).toEqual([]);
});

test('payment review works with JavaScript disabled, including native filters and details', async ({ page, browser, baseURL }) => {
  const application = await pendingApplication(page);
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport: page.viewportSize() ?? undefined });
  try {
    const nativePage = await context.newPage();
    await filteredReview(nativePage);
    await expect(nativePage.locator('#payment-review-native')).toBeVisible();
    const details = nativePage.locator('#record-' + application.id.replaceAll('-', ''));
    await details.locator('summary').click();
    await expect(details).toHaveAttribute('open', '');
    await expect(details).toContainText('Not recorded');
    await expect(nativePage.locator('.pr-page form[method="post"]')).toHaveCount(0);
    const invalid = await nativePage.request.get('/Operations?status=99');
    expect(invalid.status()).toBe(400);
    const mutation = await nativePage.request.post('/Operations');
    expect(mutation.status()).toBe(405);
  } finally {
    await context.close();
  }
  expect(application.financialRequests).toEqual([]);
});
