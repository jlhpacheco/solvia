import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

// Browser contract tests for the external SDK boundary. Provider callbacks and
// responses are deliberately simulated; these tests do not prove payment.
// These journeys include form navigation plus bounded loading/rendering. Keep
// the page assertions strict while allowing cold-start setup on slower hosts.
test.describe.configure({ timeout: 60_000 });
type ButtonMode = 'ready' | 'missing' | 'render-rejects' | 'render-throws' | 'ineligible' | 'load' | 'render-never-resolves';
const sdkRequest = /https:\/\/www\.paypal\.com\/sdk\/js\?/;
const sdkResponse = `window.paypal = {
  Buttons(options) {
    window.checkoutCallbacks = options;
    return { isEligible: () => true, render: (selector) => {
      const button = document.createElement('button');
      button.textContent = 'PayPal SDK option';
      document.querySelector(selector).append(button);
      return Promise.resolve();
    } };
  }
};`;

async function openCheckout(page: Page, buttonMode: ButtonMode = 'ready') {
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  await page.getByLabel('2 · Your car', { exact: false }).fill(
    `blue Honda Civic, license plate T${randomUUID().replaceAll('-', '').slice(0, 6)}`);
  await page.getByRole('button', { name: 'Read my answers' }).click();
  await page.getByRole('button', { name: 'Yes — show my total' }).click();
  await page.getByRole('button', { name: 'Pay with PayPal', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
  await page.evaluate(mode => {
    const container = document.createElement('div');
    container.id = 'paypal-button-container';
    document.getElementById('checkout-payment')!.append(container);
    if (mode === 'missing' || mode === 'load') {
      delete (window as any).paypal;
      if (mode === 'load') document.getElementById('checkout-payment')!.dataset.paypalSdkUrl =
        'https://www.paypal.com/sdk/js?client-id=SDK-CONTRACT&currency=USD&intent=capture&enable-funding=card';
      return;
    }
    (window as any).paypal = {
      Buttons(options: unknown) {
        (window as any).checkoutCallbacks = options;
        return {
          isEligible: () => mode !== 'ineligible',
          render: () => {
            if (mode === 'render-throws') throw new Error('Simulated SDK render failure');
            if (mode === 'render-rejects') return Promise.reject(new Error('Simulated SDK render failure'));
            if (mode === 'render-never-resolves') return new Promise<void>(resolve => { (window as any).finishSdkRender = resolve; });
            const button = document.createElement('button');
            button.textContent = 'PayPal SDK option';
            container.append(button);
            return Promise.resolve();
          }
        };
      }
    };
  }, buttonMode);
  await page.addScriptTag({ url: '/js/checkout.js' });
}

test('a delayed provider script shows native options when it loads', async ({ page }) => {
  let release!: () => void;
  const released = new Promise<void>(resolve => { release = resolve; });
  await page.route(sdkRequest, async route => {
    await released;
    await route.fulfill({ contentType: 'text/javascript', body: sdkResponse });
  });
  await openCheckout(page, 'load');
  await expect(page.locator('#checkout-feedback')).toContainText('Loading PayPal');
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  release();
  await expect(page.getByRole('button', { name: 'PayPal SDK option', exact: true })).toBeVisible();
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  await expect(page.locator('#checkout-feedback')).toBeHidden();
});

test('an aborted provider script exposes the protected backup', async ({ page }) => {
  await page.route(sdkRequest, route => route.abort('failed'));
  await openCheckout(page, 'load');
  await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
  await expect(page.locator('#checkout-feedback')).toContainText('Continue to PayPal');
});

test('a hung provider script times out and a late load cannot duplicate options', async ({ page }) => {
  await page.clock.install();
  let release!: () => void;
  const released = new Promise<void>(resolve => { release = resolve; });
  await page.route(sdkRequest, async route => {
    await released;
    await route.fulfill({ contentType: 'text/javascript', body: sdkResponse });
  });
  await openCheckout(page, 'load');
  await page.clock.runFor(12_001);
  await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
  release();
  await expect.poll(() => page.evaluate(() => typeof (window as any).paypal)).toBe('object');
  await expect(page.getByRole('button', { name: 'PayPal SDK option', exact: true })).toHaveCount(0);
  await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
});

test('a hung render times out and its late completion leaves only the backup', async ({ page }) => {
  await page.clock.install();
  await openCheckout(page, 'render-never-resolves');
  await page.clock.runFor(12_001);
  await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
  await page.evaluate(() => (window as any).finishSdkRender());
  await expect(page.locator('#paypal-button-container')).toBeHidden();
  await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
});

test('provider buttons are primary without a duplicate Solvia pay button', async ({ page }) => {
  await openCheckout(page);
  await expect(page.getByRole('button', { name: 'PayPal SDK option', exact: true })).toBeVisible();
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Continue to PayPal', exact: true })).toBeHidden();
  await expect(page.getByRole('button', { name: /^Pay \$/ })).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).checkoutCallbacks.style)).toMatchObject({
    layout: 'vertical', color: 'gold', shape: 'rect', label: 'paypal'
  });
});

for (const mode of ['missing', 'render-rejects', 'render-throws', 'ineligible'] as const) {
  test(`${mode} provider buttons expose the protected PayPal backup`, async ({ page }) => {
    await openCheckout(page, mode);
    await expect(page.locator('#paypal-button-container')).toBeHidden();
    await expect(page.locator('#paypal-redirect-fallback')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Continue to PayPal', exact: true })).toBeVisible();
    await expect(page.locator('#plain-pay-form')).toHaveAttribute('action', /\/Checkout\/Start\/[a-f0-9-]+/);
    expect(await page.locator('#plain-pay-form input[name="__RequestVerificationToken"]').inputValue()).not.toBe('');
    await expect(page.locator('#checkout-feedback')).toContainText('Continue to PayPal');
    await expect(page.locator('#checkout-feedback')).toBeFocused();
  });
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
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
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

test('an interrupted capture preserves the order and refuses a success receipt', async ({ page }) => {
  await openCheckout(page);
  const checkout = page.url();
  await page.route('**/api/paypal/orders', route => route.fulfill({ json: { id: 'ORDER-RETRY', status: 'created' } }));
  await page.evaluate(() => (window as any).checkoutCallbacks.createOrder());
  await page.route('**/api/paypal/capture', route => route.abort('failed'));
  await page.evaluate(() => (window as any).checkoutCallbacks.onApprove({ orderID: 'ORDER-RETRY' }));
  await expect(page).toHaveURL(checkout);
  await expect(page.locator('#paypal-order-id')).toHaveText('ORDER-RETRY');
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
  await expect(page.locator('#checkout-feedback')).toContainText('Connection interrupted');
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  await expect(page.getByRole('link', { name: 'Download permit (PDF)' })).toHaveCount(0);
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
