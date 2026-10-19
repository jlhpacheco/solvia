import { test, expect, request as api, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

// Integration evidence, not mocks. Opt-in only; no credentials or real card data
// belong in this file, logs, reports, screenshots, or source control.
const enabled = process.env.SOLVIA_SANDBOX_TESTS === '1';
const provider = 'https://api-m.sandbox.paypal.com';
const applicationOrigin = 'http://localhost:5156';
const evidenceDirectory = path.resolve('sandbox-results/evidence');
const campuses = [
  { name: 'CSU Monterey Bay', term: 22950, day: 500 },
  { name: 'CSU East Bay', term: 19500, day: 1000 },
  { name: 'Fresno City College', term: 3000, day: 100 },
  { name: 'Modesto Junior College / Columbia College / YCCD', term: 0, day: 200 }
];
let paypal: APIRequestContext;
let verifier: APIRequestContext;
let bearer: string;
let merchantId: string;

test.skip(!enabled, 'Set SOLVIA_SANDBOX_TESTS=1 and Sandbox credentials to run actual test transactions.');

test.beforeAll(async () => {
  const clientId = process.env.PAYPAL_SANDBOX_CLIENT_ID;
  const secret = process.env.PAYPAL_SANDBOX_CLIENT_SECRET;
  merchantId = process.env.PAYPAL_SANDBOX_MERCHANT_ID ?? '';
  if (!clientId || !secret || !merchantId) throw new Error('Sandbox client credentials and expected seller merchant ID are required.');
  mkdirSync(evidenceDirectory, { recursive: true });
  paypal = await api.newContext({ baseURL: provider });
  verifier = await api.newContext({ baseURL: applicationOrigin });
  const response = await providerCall('Sandbox OAuth', () => paypal.post('/v1/oauth2/token', {
    headers: { Authorization: `Basic ${Buffer.from(`${clientId}:${secret}`).toString('base64')}` },
    form: { grant_type: 'client_credentials' }
  }));
  expect(response.status(), 'Sandbox OAuth').toBe(200);
  bearer = (await response.json()).access_token;
});

test.afterAll(async () => {
  await paypal?.dispose();
  await verifier?.dispose();
});

// APIRequestContext includes request headers in transport-error call logs.
// Replace those errors before Playwright can serialize client credentials,
// access tokens, or the test card into a report.
async function providerCall(operation: string, send: () => Promise<APIResponse>): Promise<APIResponse> {
  try {
    return await send();
  } catch {
    throw new Error(`${operation} failed before a response was received; provider request details were redacted.`);
  }
}

async function formToken(page: Page): Promise<string> {
  const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  expect(token.length).toBeGreaterThan(20);
  return token;
}

async function draft(page: Page, person: string, college: string, daily: boolean, term = 'Fall') {
  await page.goto('/');
  await page.getByRole('button', { name: person, exact: true }).click();
  await page.goto(`/Permits/Questions?college=${encodeURIComponent(college)}`);
  const plate = 'SB' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.locator('#carText').fill(`white Volvo 760 and license plate number ${plate}`);
  if (daily) await page.getByText('Day pass — one day', { exact: true }).click();
  if (term !== 'Fall') await page.getByText(term, { exact: true }).click();
  await page.getByRole('button', { name: 'Read my answers' }).click();
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue(plate);
  await page.getByRole('button', { name: 'Yes — show my total' }).click();
  const applicationId = page.url().match(/[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}/i)?.[0];
  expect(applicationId, 'Persisted permit application').toBeTruthy();
  return { applicationId: applicationId!, plate };
}

for (const campus of campuses) {
  for (const person of ['Maria', 'Pat']) {
    for (const daily of [false, true]) {
      test(`${campus.name} | ${person === 'Maria' ? 'Student' : 'Public'} | ${daily ? 'day' : 'term'}`, async ({ page }, info) => {
        const amount = ((daily ? campus.day : campus.term) / 100).toFixed(2);
        const { applicationId, plate } = await draft(page, person, campus.name, daily);
        const category = person === 'Maria' ? 'Student' : 'Public';
        await expect(page.locator('main')).toContainText(category);
        await expect(page.locator('main')).toContainText(amount === '0.00' ? '$0' : `$${amount}`);
        let orderId: string | null = null;
        let captureId: string | null = null;
        let receiptNumber: string | null = null;
        let captureCount = 0;
        const key = `${campuses.indexOf(campus) + 1}-${category.toLowerCase()}-${daily ? 'day' : 'term'}`;
        const evidencePath = path.join(evidenceDirectory, `${key}.json`);
        const saveProgress = (stage: string) => writeFileSync(evidencePath, JSON.stringify({
          checkedAtUtc: new Date().toISOString(), stage, status: 'IN_PROGRESS', college: campus.name,
          category, person, permit: daily ? 'day' : 'term', plate, amount, currency: 'USD',
          applicationId, orderId, captureId, receiptNumber, captureCount,
          walletUiApprovalVerified: false
        }, null, 2));
        // A later receipt/PDF assertion must not hide an actual completed
        // provider transaction and encourage a blind second purchase.
        saveProgress(amount === '0.00' ? 'free-issued' : 'draft-created');
        if (amount !== '0.00') {
          await page.getByRole('button', { name: 'Pay with PayPal', exact: true }).click();
          await expect(page.getByRole('heading', { name: 'Checkout', exact: true })).toBeVisible();
          await expect(page.locator('main')).toContainText(`$${amount}`);
          const token = await formToken(page);
          const headers = { RequestVerificationToken: token };
          const started = await page.request.post('/api/paypal/orders', { headers, data: { permitApplicationId: applicationId } });
          expect(started.status(), 'Solvia order creation').toBe(200);
          orderId = (await started.json()).id;
          expect(orderId).toMatch(/^[A-Z0-9]+$/);
          saveProgress('order-created');
          const repeated = await page.request.post('/api/paypal/orders', { headers, data: { permitApplicationId: applicationId } });
          expect(repeated.status()).toBe(200);
          expect((await repeated.json()).id, 'Create retry reuses order').toBe(orderId);
          const providerHeaders = { Authorization: `Bearer ${bearer}` };
          const beforeResponse = await providerCall('Sandbox order lookup', () => paypal.get(`/v2/checkout/orders/${orderId}`, { headers: providerHeaders }));
          expect(beforeResponse.status()).toBe(200);
          const before = await beforeResponse.json();
          expect(before.purchase_units).toHaveLength(1);
          expect(before.purchase_units[0].amount).toEqual({ currency_code: 'USD', value: amount });
          expect(before.purchase_units[0].payee.merchant_id, 'Configured Solvia Sandbox seller').toBe(merchantId);
          // Official PayPal Sandbox card. This isolated test runner never adds
          // raw-card collection to Solvia or stores payment information.
          const confirmed = await providerCall('Sandbox test-card confirmation', () => paypal.post(`/v2/checkout/orders/${orderId}/confirm-payment-source`, {
            headers: { ...providerHeaders, 'PayPal-Request-Id': `solvia-matrix-confirm-${orderId}` },
            data: { payment_source: { card: {
              number: '4012888888881881', expiry: '2030-12', security_code: '123', name: `${person} Sandbox`,
              billing_address: { address_line_1: '123 Test Street', admin_area_1: 'CA', admin_area_2: 'San Jose', postal_code: '95131', country_code: 'US' }
            } } }
          }));
          expect(confirmed.ok(), 'Official Sandbox test-card approval').toBe(true);
          expect((await confirmed.json()).status).toBe('APPROVED');
          saveProgress('test-card-approved');
          const captured = await page.request.post('/api/paypal/capture', { headers, data: { orderId } });
          expect(captured.status(), 'Solvia REST capture').toBe(200);
          const result = await captured.json();
          expect(result.completed).toBe(true);
          expect(result.status).toBe('COMPLETED');
          captureId = result.captureId;
          receiptNumber = result.receiptNumber;
          saveProgress('capture-completed');
          expect(captureId).toMatch(/^[A-Z0-9]+$/);
          expect(receiptNumber).toMatch(/^SOL-/);
          const again = await page.request.post('/api/paypal/capture', { headers, data: { orderId } });
          expect(again.status()).toBe(200);
          expect(await again.json(), 'Capture retry returns same durable references').toMatchObject({ completed: true, captureId, receiptNumber });
          const afterResponse = await providerCall('Sandbox completed-order lookup', () => paypal.get(`/v2/checkout/orders/${orderId}`, { headers: providerHeaders }));
          expect(afterResponse.status()).toBe(200);
          const after = await afterResponse.json();
          expect(after.status).toBe('COMPLETED');
          const captures = after.purchase_units[0].payments.captures;
          captureCount = captures.length;
          expect(captures, 'Only one capture after retry').toHaveLength(1);
          expect(captures[0]).toMatchObject({ id: captureId, status: 'COMPLETED', amount: { value: amount, currency_code: 'USD' } });
          saveProgress('provider-verified');
          await page.goto(`/Permits/Receipt/${applicationId}`);
          await expect(page.locator('main')).toContainText(orderId!);
          await expect(page.locator('main')).toContainText(captureId!);
        }
        await expect(page.getByRole('heading', { name: /Permit live/ })).toBeVisible();
        await expect(page.locator('main')).toContainText(plate);
        await expect(page.locator('main')).toContainText(campus.name);
        if (amount === '0.00') {
          await expect(page.locator('main')).not.toContainText('PayPal order ID');
          await expect(page.locator('main')).not.toContainText('PayPal capture ID');
          receiptNumber = (await page.locator('main .display-5').textContent())?.trim() ?? null;
          expect(receiptNumber).toMatch(/^SOL-/);
        }
        const pdf = await page.request.get(`/Permits/PermitPdf/${applicationId}`);
        expect(pdf.status()).toBe(200);
        expect(pdf.headers()['content-type']).toContain('application/pdf');
        const bytes = await pdf.body();
        expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
        writeFileSync(path.join(evidenceDirectory, `${key}.pdf`), bytes);
        const verifyUrl = await page.getByRole('link', { name: 'Check this permit', exact: true }).getAttribute('href');
        expect(verifyUrl).toContain('/verify?t=');
        const verifyAddress = new URL(verifyUrl!, applicationOrigin);
        expect(verifyAddress.origin, 'Permit token stays on the local app').toBe(applicationOrigin);
        // The public permit check must work without the buyer's demo cookie.
        const checked = await verifier.get(verifyAddress.href);
        expect(checked.status()).toBe(200);
        const verifyHtml = await checked.text();
        expect(verifyHtml).toContain('verify-valid');
        expect(verifyHtml).toContain('✔ VALID');
        expect(verifyHtml, 'Public check does not expose the license plate').not.toContain(plate);
        const evidence = { checkedAtUtc: new Date().toISOString(), college: campus.name, category, person, permit: daily ? 'day' : 'term', plate, amount, currency: 'USD', applicationId, orderId, captureId, receiptNumber, captureCount, status: amount === '0.00' ? 'FREE-ISSUED' : 'COMPLETED', orderRetryReused: amount !== '0.00', captureRetryReused: amount !== '0.00', pdf: `${key}.pdf`, walletUiApprovalVerified: false, method: amount === '0.00' ? 'Free issue, no PayPal transaction' : 'Official Sandbox card confirmation and Solvia REST capture' };
        writeFileSync(evidencePath, JSON.stringify(evidence, null, 2));
        if (person === 'Pat' && campus.name === 'CSU Monterey Bay' && !daily) await page.screenshot({ path: path.join(evidenceDirectory, 'public-paid-receipt.png'), fullPage: true });
        await info.attach('sanitized-payment-proof', { body: JSON.stringify(evidence), contentType: 'application/json' });
      });
    }
  }
}

for (const daily of [false, true]) {
  test(`employee ${daily ? 'day' : 'term'} is exempt, with no PayPal order`, async ({ page }) => {
    const { applicationId } = await draft(page, 'Elena', 'CSU Monterey Bay', daily);
    await expect(page.getByRole('heading', { name: /Permit live/ })).toBeVisible();
    await expect(page.locator('main')).toContainText('EXEMPT (Employee)');
    await expect(page.locator('main')).not.toContainText('PayPal order ID');
    expect((await page.request.get(`/Permits/PermitPdf/${applicationId}`)).status()).toBe(200);
  });
}

for (const person of ['Maria', 'Pat']) {
  test(`${person} cannot buy an unpublished summer fee`, async ({ page }) => {
    await draft(page, person, 'CSU Bakersfield', false, 'Summer');
    await expect(page.getByRole('heading', { name: 'CSU Bakersfield does not publish a Summer price' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Pay with PayPal', exact: true })).toHaveCount(0);
  });
}
