import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

// Real local garage/planner/purchase records, with the isolated provider-off
// server from playwright.config.ts. These journeys never approve a payment.
test.describe.configure({ timeout: 90_000 });

test.beforeEach(({ browserName }) => {
  // The first concurrent WebKit run exhausted its cumulative 90-second
  // journey budget after the duplicate submission returned the same Review
  // URL. Keep every assertion deadline and payment/state check unchanged.
  if (browserName === 'webkit') test.setTimeout(180_000);
});

const dayNames = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const calendarDay = 86_400_000;
const financialOrReader = /\/Planner\/Read(?:[/?]|$)|\/Permits\/Understand(?:[/?]|$)|\/api\/(?:chat|paypal|prepaid)(?:[/?]|$)|\/(?:Checkout|Prepaid)\/Start\//i;

function springDates() {
  const start = new Date(Date.UTC(new Date().getFullYear() + 1, 0, 20));
  const dates = Array.from({ length: 7 }, (_, index) => new Date(start.getTime() + index * calendarDay));
  return {
    start: dates[0].toISOString().slice(0, 10),
    end: dates[6].toISOString().slice(0, 10),
    visits: dates.filter(date => [1, 3, 5].includes(date.getUTCDay())).map(date => date.toISOString().slice(0, 10))
  };
}

async function guardProviders(page: Page) {
  const blocked: string[] = [];
  await page.route(financialOrReader, async route => {
    blocked.push(`${route.request().method()} ${new URL(route.request().url()).pathname}`);
    await route.abort();
  });
  await page.route(/^https?:\/\/(?:(?:[^/]*\.)?(?:together\.ai|paypal\.com|paypalobjects\.com)|example\.invalid)(?:[/:]|$)/i, async route => {
    blocked.push(`${route.request().method()} external provider`);
    await route.abort();
  });
  return blocked;
}

async function selectPerson(page: Page, person: 'Maria' | 'Pat') {
  await page.goto('/');
  await page.getByRole('button', { name: person, exact: true }).click();
  await expect(page.getByRole('heading', { name: `Welcome, ${person}.`, exact: true })).toBeVisible();
}

async function selectDatesAndVehicle(page: Page, plate: string, selected: string[]) {
  const vehicle = page.getByLabel('Vehicle', { exact: true });
  const option = vehicle.locator('option').filter({ hasText: plate });
  await expect(option).toHaveCount(1);
  const vehicleId = await option.getAttribute('value');
  expect(vehicleId).toMatch(/^[a-f0-9-]{36}$/i);
  await vehicle.selectOption(vehicleId!);
  const checkboxes = page.getByTestId('prepaid-dates').getByRole('checkbox');
  await expect(checkboxes).toHaveCount(3);
  for (const checkbox of await checkboxes.all()) {
    await checkbox.setChecked(selected.includes(await checkbox.inputValue()));
  }
  await page.getByRole('button', { name: 'Review these dates', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Review your prepaid passes', exact: true })).toBeVisible();
}

async function pendingPurchase(page: Page, person: 'Maria' | 'Pat' = 'Maria') {
  const blocked = await guardProviders(page);
  await selectPerson(page, person);
  const plate = 'PP' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.goto('/Vehicles/Create');
  await page.getByLabel('License plate', { exact: true }).fill(plate);
  await page.getByLabel('Make', { exact: true }).selectOption('Honda');
  await page.getByLabel('Model', { exact: true }).selectOption('Civic');
  await page.getByRole('button', { name: 'Save vehicle', exact: true }).click();
  await expect(page.locator('main')).toContainText(plate);

  const range = springDates();
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('Fresno City College');
  await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
  await page.getByLabel('Spring', { exact: true }).check();
  await page.getByLabel('Start of your planning period', { exact: true }).fill(range.start);
  await page.getByLabel('End date', { exact: true }).fill(range.end);
  for (const name of dayNames) {
    await page.getByLabel(name, { exact: true }).setChecked(['Monday', 'Wednesday', 'Friday'].includes(name));
  }
  await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
  await expect(page.getByTestId('daily-total')).toHaveText('$3.00');
  await page.getByRole('button', { name: 'Buy these dates', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Choose your prepaid dates', exact: true })).toBeVisible();
  const chooseUrl = page.url();
  const selected = range.visits.slice(0, 2);
  await selectDatesAndVehicle(page, plate, selected);
  const panel = page.locator('#checkout-payment');
  const id = await panel.getAttribute('data-application-id');
  expect(id).toMatch(/^[a-f0-9-]{36}$/i);
  const reviewUrl = page.url();
  await expect(panel).toHaveAttribute('data-payment-kind', 'prepaid');
  await expect(panel).toHaveAttribute('data-create-order-url', '/api/prepaid/orders');
  await expect(panel).toHaveAttribute('data-capture-url', '/api/prepaid/capture');
  await expect(panel).toHaveAttribute('data-start-url', `/Prepaid/Start/${id}`);
  await expect(panel).toHaveAttribute('data-done-url', `/Prepaid/Receipt/${id}`);
  return { id: id!, plate, selected, omitted: range.visits[2], chooseUrl, reviewUrl, blocked };
}

for (const person of ['Maria', 'Pat'] as const) {
  test(`@smoke ${person} reviews exactly the selected prepaid dates and cannot download pending permits`, async ({ page }) => {
    const purchase = await pendingPurchase(page, person);
    await expect(page.getByTestId('prepaid-per-day')).toContainText('$1.00');
    await expect(page.getByTestId('prepaid-total')).toContainText('$2.00');
    const dates = page.getByTestId('prepaid-review-dates');
    await expect(dates.locator('time')).toHaveCount(2);
    for (const date of purchase.selected) await expect(dates.locator(`time[datetime="${date}"]`)).toHaveCount(1);
    await expect(dates.locator(`time[datetime="${purchase.omitted}"]`)).toHaveCount(0);
    await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
    await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
    await expect(page.locator('a[href^="/Prepaid/Pdf/"]')).toHaveCount(0);
    await expect(page.getByTestId('fastlane-sandbox-help')).toContainText('111111');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    const response = await page.goto(`/Prepaid/Pdf/${purchase.id}`);
    expect(response?.status()).toBe(400);
    expect(response?.headers()['content-type']).not.toContain('application/pdf');
    await page.goto(`/Prepaid/Receipt/${purchase.id}`);
    await expect(page).toHaveURL(purchase.reviewUrl);
    await expect(page.locator('a[href^="/Prepaid/Pdf/"]')).toHaveCount(0);
    expect(purchase.blocked, 'Planning and review must not invoke extraction, orders, captures or providers').toEqual([]);
  });
}

test('repeating the same selected-date submission returns the same pending purchase', async ({ page }) => {
  const purchase = await pendingPurchase(page);
  await page.goto(purchase.chooseUrl);
  await expect(page.getByRole('heading', { name: 'Choose your prepaid dates', exact: true })).toBeVisible();
  await selectDatesAndVehicle(page, purchase.plate, purchase.selected);
  await expect(page).toHaveURL(purchase.reviewUrl);
  await expect(page.locator('#checkout-payment')).toHaveAttribute('data-application-id', purchase.id);
  await expect(page.getByTestId('prepaid-total')).toContainText('$2.00');
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
  expect(purchase.blocked).toEqual([]);
});

test('another demo profile cannot open a prepaid purchase, receipt or packet', async ({ page }) => {
  const purchase = await pendingPurchase(page, 'Maria');
  await selectPerson(page, 'Pat');
  for (const endpoint of ['Review', 'Receipt', 'Pdf']) {
    const response = await page.goto(`/Prepaid/${endpoint}/${purchase.id}`);
    expect(response?.status()).toBe(404);
    await expect(page.locator('body')).not.toContainText(purchase.plate);
  }
  expect(purchase.blocked).toEqual([]);
});

test('no-JavaScript prepaid continuation rejects missing CSRF or review snapshot without creating an order', async ({ page, browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const purchase = await pendingPurchase(page);
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    // Carry the same real test-browser identity and anti-forgery cookies into
    // a no-JS window. This neither signs in as another person nor pays anything.
    await context.addCookies(await page.context().cookies());
    const nativePage = await context.newPage();
    const blocked = await guardProviders(nativePage);
    await nativePage.goto(purchase.reviewUrl);
    await expect(nativePage.getByRole('heading', { name: 'Review your prepaid passes', exact: true })).toBeVisible();
    await expect(nativePage.getByTestId('prepaid-review-dates').locator('time')).toHaveCount(2);
    const form = nativePage.locator('#plain-pay-form');
    const startPath = `/Prepaid/Start/${purchase.id}`;
    await expect(form).toHaveAttribute('action', startPath);
    const token = await form.locator('input[name="__RequestVerificationToken"]').inputValue();
    const snapshot = await form.locator('input[name="reviewedSnapshot"]').inputValue();
    expect(token).not.toBe('');
    expect(snapshot).toMatch(/^[a-f0-9]{64}$/i);
    // Deliberately malformed local HTML POSTs exercise server rejection. No
    // valid payment-start request is sent, and redirects are not followed.
    const missingCsrf = await context.request.post(startPath, { form: { reviewedSnapshot: snapshot }, maxRedirects: 0 });
    expect(missingCsrf.status()).toBe(400);
    const missingSnapshot = await context.request.post(startPath, { form: { __RequestVerificationToken: token }, maxRedirects: 0 });
    expect(missingSnapshot.status()).toBe(302);
    expect(missingSnapshot.headers().location).toBe(`/Prepaid/Review/${purchase.id}`);
    await nativePage.goto(purchase.reviewUrl);
    await expect(nativePage.getByRole('alert').filter({ hasText: 'details or total changed' })).toBeVisible();
    await expect(nativePage.locator('#paypal-order-id')).toContainText('Not created yet');
    await expect(nativePage.locator('#paypal-capture-id')).toContainText('Not issued yet');
    await expect(nativePage.locator('a[href^="/Prepaid/Pdf/"]')).toHaveCount(0);
    expect(blocked).toEqual([]);
    expect(purchase.blocked).toEqual([]);
  } finally { await context.close(); }
});

test('controlled SDK callbacks use prepaid routes and snapshots while the real receipt still rejects unpaid records', async ({ page }) => {
  // This is an explicit external-boundary simulation, not a real PayPal SDK,
  // approved buyer or verified capture. The application record remains unpaid.
  const purchase = await pendingPurchase(page);
  await page.waitForLoadState('load');
  const snapshot = await page.locator('#checkout-payment').getAttribute('data-reviewed-snapshot');
  expect(snapshot).toMatch(/^[a-f0-9]{64}$/i);
  await expect(page.locator('#paypal-button-container')).toHaveCount(0);
  await page.evaluate(() => {
    const panel = document.getElementById('checkout-payment')!;
    panel.dataset.createOrderUrl = 'https://example.invalid/not-a-payment-endpoint';
    panel.dataset.captureUrl = '/api/paypal/capture';
    panel.dataset.startUrl = 'https://example.invalid/not-a-payment-endpoint';
    panel.dataset.doneUrl = '/Checkout/Done/not-the-prepaid-receipt';
    const container = document.createElement('div');
    container.id = 'paypal-button-container';
    panel.append(container);
    (window as any).paypal = {
      Buttons(options: unknown) {
        (window as any).prepaidSdkCallbacks = options;
        return { isEligible: () => true, render: () => Promise.resolve() };
      }
    };
  });
  await page.addScriptTag({ url: '/js/checkout.js' });
  await expect.poll(() => page.evaluate(() => typeof (window as any).prepaidSdkCallbacks?.createOrder)).toBe('function');
  await expect(page.locator('#plain-pay-form')).toHaveAttribute('action', `/Prepaid/Start/${purchase.id}`);
  let submitted: Record<string, unknown> | undefined;
  let verificationToken = '';
  await page.route('**/api/prepaid/orders', route => {
    submitted = route.request().postDataJSON();
    verificationToken = route.request().headers()['requestverificationtoken'] ?? '';
    return route.fulfill({ json: { id: 'CONTROLLED-PREPAID-ORDER', status: 'created', purchaseId: purchase.id } });
  });
  expect(await page.evaluate(() => (window as any).prepaidSdkCallbacks.createOrder())).toBe('CONTROLLED-PREPAID-ORDER');
  expect(submitted).toEqual({ purchaseId: purchase.id, reviewedSnapshot: snapshot });
  expect(verificationToken).not.toBe('');
  await page.evaluate(() => (window as any).prepaidSdkCallbacks.onCancel());
  await expect(page.locator('#paypal-order-id')).toHaveText('CONTROLLED-PREPAID-ORDER');
  await expect(page.locator('#checkout-feedback')).toContainText('resume the same order');
  let captureBody: unknown;
  await page.route('**/api/prepaid/capture', route => {
    captureBody = route.request().postDataJSON();
    return route.fulfill({ json: { completed: false, status: 'PENDING', orderId: 'CONTROLLED-PREPAID-ORDER', captureId: 'CONTROLLED-PENDING' } });
  });
  await page.evaluate(() => (window as any).prepaidSdkCallbacks.onApprove({ orderID: 'CONTROLLED-PREPAID-ORDER' }));
  expect(captureBody).toEqual({ orderId: 'CONTROLLED-PREPAID-ORDER' });
  await expect(page).toHaveURL(purchase.reviewUrl);
  await expect(page.locator('#checkout-feedback')).toContainText('These passes have not been issued');
  await expect(page.locator('a[href^="/Prepaid/Pdf/"]')).toHaveCount(0);
  await page.route('**/api/prepaid/orders', route => route.fulfill({ status: 409,
    json: { error: 'Review these dates and the updated total before retrying.', code: 'REVIEW_CHANGED' } }));
  await page.evaluate(async () => {
    try { await (window as any).prepaidSdkCallbacks.createOrder(); } catch { /* Controlled SDK calls onError next. */ }
    (window as any).prepaidSdkCallbacks.onError();
  });
  await expect(page.locator('#checkout-feedback')).toContainText('REVIEW_CHANGED');
  const receiptRequests: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).pathname === `/Prepaid/Receipt/${purchase.id}`) receiptRequests.push(request.method());
  });
  await page.route('**/api/prepaid/capture', route => route.fulfill({ json: {
    completed: true, status: 'COMPLETED', captureId: 'CONTROLLED-NOT-A-REAL-CAPTURE'
  } }));
  await page.evaluate(() => { void (window as any).prepaidSdkCallbacks.onApprove({ orderID: 'CONTROLLED-PREPAID-ORDER' }); });
  await expect.poll(() => receiptRequests).toEqual(['GET']);
  await expect(page).toHaveURL(purchase.reviewUrl);
  await expect(page.locator('a[href^="/Prepaid/Pdf/"]')).toHaveCount(0);
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
  expect(purchase.blocked, 'Only explicitly fulfilled boundary simulations may use financial routes').toEqual([]);
});
