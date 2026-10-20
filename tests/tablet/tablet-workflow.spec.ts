import { test, expect, type Locator, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import path from 'node:path';

// Isolated app + real local sponsor bundle and PDF bytes. External payment UI
// below is deliberately simulated: no provider approval, capture or charge.
const gridBundle = path.resolve(__dirname, '../../node_modules/ag-grid-community/dist/ag-grid-community.min.js');
let browserErrors: string[];
let paymentRequests: string[];

test.beforeEach(async ({ page }) => {
  browserErrors = [];
  paymentRequests = [];
  page.on('pageerror', error => browserErrors.push(error.message));
  page.on('request', request => {
    if (request.method() === 'POST' && /\/api\/paypal\/(orders|capture)|\/Checkout\/Start\//.test(request.url())) {
      paymentRequests.push(request.url());
    }
  });
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (['127.0.0.1', 'localhost'].includes(url.hostname)) return route.continue();
    if (url.pathname.endsWith('/ag-grid-community@34.3.1/dist/ag-grid-community.min.js')) {
      return route.fulfill({ path: gridBundle, contentType: 'text/javascript' });
    }
    // Safety boundary: these responsive tests cannot contact a remote provider.
    return route.abort('blockedbyclient');
  });
});

test.afterEach(() => {
  expect(browserErrors, 'Uncaught browser errors').toEqual([]);
  expect(paymentRequests, 'Responsive journeys must not attempt a payment').toEqual([]);
});

async function fitsViewport(page: Page) {
  await expect.poll(() => page.evaluate(() =>
    document.documentElement.scrollWidth <= window.innerWidth + 1),
  { message: 'No page-wide horizontal scrolling at this tablet width' }).toBe(true);
}

async function tapPrimary(locator: Locator) {
  await expect(locator).toBeVisible();
  const box = await locator.boundingBox();
  expect(box, 'Primary action has a rendered touch target').not.toBeNull();
  expect(box!.height, 'Primary action touch target height').toBeGreaterThanOrEqual(44);
  expect(box!.width, 'Primary action touch target width').toBeGreaterThanOrEqual(44);
  await locator.tap();
}

async function answers(page: Page, person: 'Maria' | 'Pat' | 'Elena', daily: boolean) {
  await page.goto('/');
  await page.getByRole('button', { name: person, exact: true }).tap();
  await expect(page.getByRole('heading', { name: `Welcome, ${person}.`, exact: true })).toBeVisible();
  await fitsViewport(page);
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  const plate = 'T' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.getByLabel('2 · Your car', { exact: false }).fill(`blue Honda Civic, license plate ${plate}`);
  if (daily) await page.locator('label[for="typeDaily"]').tap();
  await fitsViewport(page);
  await tapPrimary(page.getByRole('button', { name: 'Read my answers' }));
  await expect(page.getByRole('heading', { name: 'Did I get this right?' })).toBeVisible();
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue(plate);
  await fitsViewport(page);
  await tapPrimary(page.getByRole('button', { name: 'Yes — show my total' }));
  return plate;
}

async function paymentControls(page: Page, mode: 'ready' | 'reject') {
  // Same controlled SDK boundary as checkout-errors.spec.ts. The server has no
  // client credentials, so add the SDK container and exercise the real local JS.
  await page.evaluate(selectedMode => {
    const container = document.createElement('div');
    container.id = 'paypal-button-container';
    const fallback = document.getElementById('paypal-redirect-fallback')!;
    const paymentCard = fallback.parentElement!;
    paymentCard.querySelector('[role="alert"]')?.remove();
    paymentCard.insertBefore(container, fallback);
    (window as any).paypal = {
      Buttons(options: { onCancel: () => void }) {
        return {
          isEligible: () => true,
          render() {
            if (selectedMode === 'reject') return Promise.reject(new Error('Simulated unavailable SDK'));
            for (const name of ['Simulated PayPal option', 'Simulated debit or credit card option']) {
              const button = document.createElement('button');
              button.type = 'button';
              button.textContent = name;
              button.style.cssText = 'display:block;width:100%;min-height:48px;margin-block:8px';
              button.addEventListener('click', options.onCancel);
              container.append(button);
            }
            return Promise.resolve();
          }
        };
      }
    };
  }, mode);
  await page.addScriptTag({ url: '/js/checkout.js' });
}

async function downloadPdf(page: Page, link: Locator) {
  const pending = page.waitForEvent('download');
  await link.tap();
  const download = await pending;
  expect(await download.failure()).toBeNull();
  const file = await download.path();
  expect(file).toBeTruthy();
  const bytes = readFileSync(file!);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  expect(bytes.length).toBeGreaterThan(1000);
}

test('tablet touch navigation and campus fee search fit the viewport', async ({ page }) => {
  await page.goto('/College/Fees');
  await expect(page.locator('#feeGrid .ag-root-wrapper')).toBeVisible();
  await page.getByLabel('Find a campus or fee').fill('Fresno');
  await expect(page.getByRole('status')).toHaveText('1 of 10 campuses');
  await expect(page.locator('#feeGrid .ag-center-cols-container .ag-row')).toHaveCount(1);
  await fitsViewport(page);
  await page.getByRole('navigation', { name: 'Permit workspace' })
    .getByRole('link', { name: 'Ask Solvia', exact: true }).tap();
  await page.getByRole('link', { name: 'Fresno City College $30.00 per term', exact: true }).tap();
  await expect(page.getByRole('heading', { name: 'Three quick questions' })).toBeVisible();
  await fitsViewport(page);
});

test('student term review reaches simulated wallet and card controls by touch', async ({ page }) => {
  const plate = await answers(page, 'Maria', false);
  await expect(page.getByRole('heading', { name: 'Check your total' })).toBeVisible();
  await expect(page.locator('main')).toContainText('$30.00');
  await expect(page.locator('main')).toContainText(plate);
  await fitsViewport(page);
  await tapPrimary(page.getByRole('button', { name: 'Pay with PayPal', exact: true }));
  await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
  await paymentControls(page, 'ready');
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  for (const name of ['Simulated PayPal option', 'Simulated debit or credit card option']) {
    await tapPrimary(page.getByRole('button', { name, exact: true }));
    await expect(page.locator('#checkout-feedback')).toContainText('Payment is not confirmed');
  }
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
  await expect(page.locator('#paypal-capture-id')).toContainText('Not issued yet');
  await fitsViewport(page);
});

test('visitor day pass retains a reachable protected backup after simulated SDK failure', async ({ page }) => {
  await answers(page, 'Pat', true);
  await expect(page.getByRole('heading', { name: 'Check your total' })).toBeVisible();
  await expect(page.locator('main')).toContainText('$1.00');
  await fitsViewport(page);
  await tapPrimary(page.getByRole('button', { name: 'Pay with PayPal', exact: true }));
  await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
  await paymentControls(page, 'reject');
  const backup = page.getByRole('button', { name: 'Continue to PayPal', exact: true });
  await expect(backup).toBeVisible();
  await backup.scrollIntoViewIfNeeded();
  await expect(backup).toBeInViewport({ ratio: 1 });
  await expect(page.locator('#checkout-feedback')).toBeFocused();
  expect(await page.locator('#plain-pay-form input[name="__RequestVerificationToken"]').inputValue()).not.toBe('');
  await expect(page.locator('#plain-pay-form')).toHaveAttribute('method', 'post');
  await expect(page.getByRole('link', { name: 'Download permit (PDF)', exact: true })).toHaveCount(0);
  await fitsViewport(page);
});

test('employee PDF is visible immediately and can be downloaded again from My permits', async ({ page }) => {
  const plate = await answers(page, 'Elena', false);
  await expect(page.getByRole('heading', { name: /Permit live/ })).toBeVisible();
  await expect(page.locator('main')).toContainText('EXEMPT (Employee)');
  const primary = page.getByRole('link', { name: 'Download permit (PDF)', exact: true });
  await expect(primary).toHaveCount(1);
  // Visibility alone passes even for a link far below the fold.
  await expect(primary).toBeInViewport({ ratio: 1 });
  await expect(page.locator('#permit-download-help')).toContainText('US Letter');
  await expect(page.locator('#permit-download-help')).toContainText('Actual size (100%)');
  const pdfHref = await primary.getAttribute('href');
  expect(pdfHref).toMatch(/^\/Permits\/PermitPdf\/[a-f0-9-]+$/);
  await fitsViewport(page);
  await downloadPdf(page, primary);
  await page.getByRole('link', { name: 'Back to my permits', exact: true }).tap();
  await expect(page.getByRole('heading', { name: 'My permits', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText(plate);
  await fitsViewport(page);
  // Works with either responsive cards or a table; hidden duplicate markup is
  // excluded. The issued document must remain reachable without horizontal pan.
  const recovered = page.locator(`a[href="${pdfHref}"]:visible`);
  await expect(recovered).toHaveCount(1);
  await expect(recovered).toHaveText('Permit (PDF)');
  await recovered.scrollIntoViewIfNeeded();
  await expect(recovered).toBeInViewport({ ratio: 1 });
  await downloadPdf(page, recovered);
});
