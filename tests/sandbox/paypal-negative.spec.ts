import { test, expect, request as api, type APIRequestContext, type APIResponse, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

// Opt-in real Sandbox checks. No mocks, live endpoints, or real card data.
// Official trigger and static card: https://developer.paypal.com/sandbox-testing/card-testing/
const provider = 'https://api-m.sandbox.paypal.com';
const expectedMerchant = 'W3X4FJ56DJ2CW';
const evidenceDirectory = path.resolve('sandbox-results/negative-evidence');
let paypal: APIRequestContext;
let bearer: string;

test.skip(process.env.SOLVIA_SANDBOX_TESTS !== '1', 'Requires explicit Sandbox integration opt-in and private credentials.');

// Playwright's transport error includes request headers. Replace it before
// a reporter can serialize a secret, bearer token, or Sandbox test card.
async function providerCall(operation: string, send: () => Promise<APIResponse>): Promise<APIResponse> {
  try { return await send(); }
  catch { throw new Error(`${operation} failed before a response; provider request details redacted.`); }
}

test.beforeAll(async () => {
  const clientId = process.env.PAYPAL_SANDBOX_CLIENT_ID;
  const secret = process.env.PAYPAL_SANDBOX_CLIENT_SECRET;
  if (!clientId || !secret || process.env.PAYPAL_SANDBOX_MERCHANT_ID !== expectedMerchant)
    throw new Error('Private Sandbox credentials and the expected Solvia test merchant are required.');
  mkdirSync(evidenceDirectory, { recursive: true });
  paypal = await api.newContext({ baseURL: provider, maxRedirects: 0 });
  const response = await providerCall('Sandbox OAuth', () => paypal.post('/v1/oauth2/token', {
    headers: { Authorization: `Basic ${Buffer.from(`${clientId}:${secret}`).toString('base64')}` },
    form: { grant_type: 'client_credentials' }
  }));
  expect(response.status(), 'Sandbox OAuth status').toBe(200);
  bearer = (await response.json()).access_token;
  if (!bearer) throw new Error('Sandbox OAuth returned no access token.');
});

test.afterAll(async () => { await paypal?.dispose(); });

function save(key: string, evidence: object) {
  writeFileSync(path.join(evidenceDirectory, `${key}.json`), JSON.stringify({
    checkedAtUtc: new Date().toISOString(), college: 'Fresno City College',
    person: 'Maria', category: 'Student', permit: 'day', amount: '1.00', currency: 'USD',
    ...evidence
  }, null, 2));
}

async function providerOrder(orderId: string) {
  const response = await providerCall('Sandbox order lookup', () => paypal.get(`/v2/checkout/orders/${orderId}`, {
    headers: { Authorization: `Bearer ${bearer}` }
  }));
  expect(response.status(), 'Sandbox order lookup status').toBe(200);
  const order = await response.json();
  expect(order.id, 'Provider order ID matches app order').toBe(orderId);
  expect(order.purchase_units, 'One known purchase unit').toHaveLength(1);
  expect(order.purchase_units[0].amount, 'Only the agreed $1.00 test fee').toMatchObject({ value: '1.00', currency_code: 'USD' });
  expect(order.purchase_units[0].payee.merchant_id, 'Only the Solvia test seller').toBe(expectedMerchant);
  return order;
}

async function createUnpaidDayOrder(page: Page, key: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Maria', exact: true }).click();
  await page.goto('/Permits/Questions?college=Fresno%20City%20College');
  const plate = 'NG' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
  await page.locator('#carText').fill(`white Volvo 760 and license plate number ${plate}`);
  await page.getByText('Day pass — one day', { exact: true }).click();
  await page.getByRole('button', { name: 'Read my answers' }).click();
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue(plate);
  await page.getByRole('button', { name: 'Yes — show my total' }).click();
  const applicationId = page.url().match(/[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}/i)?.[0];
  expect(applicationId, 'Persisted unpaid application').toBeTruthy();
  await expect(page.locator('main')).toContainText('$1.00');
  await expect(page.getByRole('heading', { name: 'Choose how to pay', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const headers = { RequestVerificationToken: token };
  const reviewedSnapshot = await page.locator('#checkout-payment').getAttribute('data-reviewed-snapshot');
  expect(reviewedSnapshot).toMatch(/^[a-f0-9]{64}$/);
  const created = await page.request.post('/api/paypal/orders', { headers, data: { permitApplicationId: applicationId, reviewedSnapshot } });
  expect(created.status(), 'Actual Solvia order creation').toBe(200);
  const orderId = (await created.json()).id as string;
  expect(orderId).toMatch(/^[A-Z0-9]+$/);
  save(key, { status: 'IN_PROGRESS', stage: 'unapproved-order-created', applicationId, orderId, plate });
  const order = await providerOrder(orderId);
  expect(order.status, 'Brand new order is not approved').toBe('CREATED');
  return { applicationId: applicationId!, orderId, plate, headers };
}

async function assertNoPermit(page: Page, applicationId: string) {
  await page.goto(`/Permits/Receipt/${applicationId}`);
  await expect(page.getByRole('heading', { name: 'Not paid yet', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Permit live/ })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Download permit (PDF)', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Check this permit', exact: true })).toHaveCount(0);
  const pdf = await page.request.get(`/Permits/PermitPdf/${applicationId}`);
  expect(pdf.status(), 'Unpaid owned application has no printable permit').toBe(400);
  expect(pdf.headers()['content-type']).not.toContain('application/pdf');
  expect(await pdf.text()).toContain('This permit is not live yet');
}

test('real unapproved order is rejected without payment or permit issuance', async ({ page }) => {
  const key = 'unapproved-order';
  const draft = await createUnpaidDayOrder(page, key);
  const captured = await page.request.post('/api/paypal/capture', {
    headers: draft.headers, data: { orderId: draft.orderId }
  });
  expect(captured.status(), 'Unapproved real provider order returns conflict').toBe(409);
  const result = await captured.json();
  expect(result.code).toBe('ORDER_NOT_APPROVED');
  expect(result.completed).not.toBe(true);
  const order = await providerOrder(draft.orderId);
  const captures = order.purchase_units[0].payments?.captures ?? [];
  expect(captures, 'No provider capture occurred').toHaveLength(0);
  expect(order.status).toBe('CREATED');
  await assertNoPermit(page, draft.applicationId);
  save(key, { status: 'PASSED', applicationId: draft.applicationId, orderId: draft.orderId,
    plate: draft.plate, appHttpStatus: captured.status(), appIssue: result.code,
    providerOrderStatus: order.status, captureCount: captures.length, paidPermitIssued: false,
    pdfHttpStatus: 400, method: 'Actual unapproved PayPal Sandbox order via Solvia REST capture' });
});

test('official Sandbox refused-card trigger never issues a paid permit', async ({ page }) => {
  const key = 'refused-card';
  const draft = await createUnpaidDayOrder(page, key);
  const confirmed = await providerCall('Sandbox refusal-trigger card confirmation', () => paypal.post(
    `/v2/checkout/orders/${draft.orderId}/confirm-payment-source`, {
      headers: { Authorization: `Bearer ${bearer}`, 'PayPal-Request-Id': `solvia-negative-confirm-${draft.orderId}` },
      data: { payment_source: { card: {
        number: '4012888888881881', expiry: '2030-12', security_code: '123', name: 'CCREJECT-REFUSED',
        billing_address: { address_line_1: '123 Test Street', admin_area_1: 'CA', admin_area_2: 'San Jose', postal_code: '95131', country_code: 'US' }
      } } }
    }));
  // Confirmation validates the payment source; the issuer rejection is
  // expected when Solvia actually requests capture, not a client-side mock.
  expect(confirmed.ok(), 'Official Sandbox payment-source confirmation').toBe(true);
  const confirmationStatus = (await confirmed.json()).status;
  expect(confirmationStatus).toBe('APPROVED');
  save(key, { status: 'IN_PROGRESS', stage: 'refusal-source-confirmed', applicationId: draft.applicationId,
    orderId: draft.orderId, plate: draft.plate, confirmationStatus });
  const captured = await page.request.post('/api/paypal/capture', {
    headers: draft.headers, data: { orderId: draft.orderId }
  });
  const result = await captured.json();
  expect(result.completed, 'Provider decline cannot complete app payment').not.toBe(true);
  expect(result.receiptNumber, 'No successful receipt minted').toBeFalsy();
  save(key, { status: 'IN_PROGRESS', stage: 'app-capture-returned', applicationId: draft.applicationId,
    orderId: draft.orderId, plate: draft.plate, appHttpStatus: captured.status(),
    appIssue: result.code ?? null, appStatus: result.status ?? null, completed: result.completed ?? false });
  const order = await providerOrder(draft.orderId);
  const captures = order.purchase_units[0].payments?.captures ?? [];
  const observedCaptures = captures.map((capture: { id?: string; status?: string; processor_response?: { response_code?: string } }) => ({
    id: capture.id, status: capture.status, responseCode: capture.processor_response?.response_code
  }));
  save(key, { status: 'IN_PROGRESS', stage: 'capture-result-observed', applicationId: draft.applicationId,
    orderId: draft.orderId, plate: draft.plate, appHttpStatus: captured.status(), appIssue: result.code ?? null,
    appStatus: result.status ?? null, providerOrderStatus: order.status, captures: observedCaptures });
  expect(captures.some((capture: { status?: string }) => capture.status === 'COMPLETED'),
    'No successful provider capture exists').toBe(false);
  const refusedCapture = observedCaptures.find((capture: { status?: string; responseCode?: string }) =>
    capture.status === 'DECLINED' && capture.responseCode === '0500');
  const providerDeclineCode = ['INSTRUMENT_DECLINED', 'PAYMENT_SOURCE_DECLINED'].includes(result.code);
  expect(Boolean(refusedCapture) || providerDeclineCode,
    'Actual refusal evidence: DECLINED/0500 capture or provider decline issue').toBe(true);
  if (refusedCapture) {
    expect(captured.status()).toBe(200);
    expect(result.completed).toBe(false);
    expect(result.status).toBe('DECLINED');
  } else {
    expect(captured.status()).toBe(502);
  }
  await assertNoPermit(page, draft.applicationId);
  save(key, { status: 'PASSED', applicationId: draft.applicationId, orderId: draft.orderId,
    plate: draft.plate, confirmationStatus, appHttpStatus: captured.status(), appIssue: result.code ?? null,
    appStatus: result.status ?? null, providerOrderStatus: order.status, captures: observedCaptures,
    paidPermitIssued: false, pdfHttpStatus: 400,
    method: 'Official Sandbox CCREJECT-REFUSED trigger followed by actual Solvia REST capture' });
});
