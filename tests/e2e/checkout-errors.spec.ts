import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

// Browser contract tests for the external SDK boundary. Provider callbacks and
// responses are deliberately simulated; these tests do not prove payment.
async function openCheckout(page: Page) {
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  await page.getByLabel('2 · Your car', { exact: false }).fill(
    `blue Honda Civic, license plate T${randomUUID().replaceAll('-', '').slice(0, 6)}`);
  await page.getByRole('button', { name: 'Read my answers' }).click();
  await page.getByRole('button', { name: 'Yes — show my total' }).click();
  await page.getByRole('button', { name: 'Pay with PayPal', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
  await page.evaluate(() => {
    const container = document.createElement('div');
    container.id = 'paypal-button-container';
    document.getElementById('checkout-payment')!.append(container);
    (window as any).paypal = {
      Buttons(options: unknown) {
        (window as any).checkoutCallbacks = options;
        return { render: () => Promise.resolve() };
      }
    };
  });
  await page.addScriptTag({ url: '/js/checkout.js' });
}

test('capture HTTP failure stays on checkout and displays provider error', async ({ page }) => {
  await openCheckout(page);
  const checkout = page.url();
  await page.route('**/api/paypal/capture', route => route.fulfill({
    status: 409, json: { error: 'Approve the order with a Personal buyer test account.', code: 'ORDER_NOT_APPROVED', debugId: 'TEST-REF' }
  }));
  await page.evaluate(() => (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-TEST' }));
  await expect(page).toHaveURL(checkout);
  await expect(page.locator('#checkout-feedback')).toContainText('ORDER_NOT_APPROVED');
  await expect(page.locator('#checkout-feedback')).toContainText('TEST-REF');
  await expect(page.locator('#checkout-feedback')).toBeFocused();
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
});

test('pending capture exposes references without claiming a paid permit', async ({ page }) => {
  await openCheckout(page);
  const checkout = page.url();
  await page.route('**/api/paypal/capture', route => route.fulfill({
    json: { status: 'PENDING', completed: false, orderId: 'ORDER-TEST', captureId: 'CAP-PENDING' }
  }));
  await page.evaluate(() => (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-TEST' }));
  await expect(page).toHaveURL(checkout);
  await expect(page.locator('#paypal-order-id')).toHaveText('ORDER-TEST');
  await expect(page.locator('#paypal-capture-id')).toHaveText('CAP-PENDING');
  await expect(page.locator('#checkout-feedback')).toContainText('This permit is not active');
  await expect(page.getByRole('link', { name: 'Download permit (PDF)' })).toHaveCount(0);
});

test('order creation errors remain visible after the SDK error callback', async ({ page }) => {
  await openCheckout(page);
  await page.route('**/api/paypal/orders', route => route.fulfill({
    status: 503, json: { error: 'PayPal sandbox timed out. Check your permit status before retrying.' }
  }));
  await page.evaluate(async () => {
    try { await (window as any).checkoutCallbacks.createOrder(); } catch { /* SDK calls onError next. */ }
    (window as any).checkoutCallbacks.onError();
  });
  await expect(page.locator('#checkout-feedback')).toContainText('PayPal sandbox timed out');
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
});

test('created order ID appears immediately and cancellation preserves it', async ({ page }) => {
  await openCheckout(page);
  await page.route('**/api/paypal/orders', route => route.fulfill({ json: { id: 'ORDER-TEST', status: 'created' } }));
  expect(await page.evaluate(() => (window as any).checkoutCallbacks.createOrder())).toBe('ORDER-TEST');
  await expect(page.locator('#paypal-order-id')).toHaveText('ORDER-TEST');
  await page.evaluate(() => (window as any).checkoutCallbacks.onCancel());
  await expect(page.locator('#checkout-feedback')).toContainText('resume the same order');
  await expect(page.locator('#paypal-order-id')).toHaveText('ORDER-TEST');
});

test('malformed capture response stays on checkout', async ({ page }) => {
  await openCheckout(page);
  const checkout = page.url();
  await page.route('**/api/paypal/capture', route => route.fulfill({ status: 502, contentType: 'text/html', body: '<h1>Gateway error</h1>' }));
  await page.evaluate(() => (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-TEST' }));
  await expect(page).toHaveURL(checkout);
  await expect(page.locator('#checkout-feedback')).toContainText('unreadable response');
});

test('completed flag without a capture ID does not navigate to a receipt', async ({ page }) => {
  await openCheckout(page);
  const checkout = page.url();
  await page.route('**/api/paypal/capture', route => route.fulfill({ json: { status: 'COMPLETED', completed: true, captureId: null } }));
  await page.evaluate(() => (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-TEST' }));
  await expect(page).toHaveURL(checkout);
  await expect(page.locator('#checkout-feedback')).toContainText('This permit is not active');
});

test('complete capture response opens the server receipt without bypassing its payment gate', async ({ page }) => {
  await openCheckout(page);
  await page.route('**/api/paypal/capture', route => route.fulfill({ json: { status: 'COMPLETED', completed: true, captureId: 'CAP-TEST' } }));
  await page.evaluate(() => { void (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-TEST' }); });
  await expect(page).toHaveURL(/\/Checkout\/Done\//);
  // The simulated API did not pay the real application. The receipt still
  // checks the database and refuses to claim payment or offer a permit PDF.
  await expect(page.getByRole('heading', { name: 'Not paid yet' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Download permit (PDF)' })).toHaveCount(0);
});
