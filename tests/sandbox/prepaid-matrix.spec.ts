import { test, expect, request as api, type APIResponse } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

// Opt-in real Sandbox REST verification. No live payment, provider mocks or AI calls.
test.skip(process.env.SOLVIA_SANDBOX_TESTS !== '1', 'Explicit Sandbox opt-in and private credentials required.');
const evidenceDir = path.resolve('sandbox-results/prepaid');
const cases = [{ person: 'Maria', campus: 'Fresno City College', unit: 100 },
  { person: 'Pat', campus: 'CSU Monterey Bay', unit: 500 }];
async function safeCall(operation: string, send: () => Promise<APIResponse>) {
  try { return await send(); } catch { throw new Error(`${operation} transport failed; provider request details redacted.`); }
}

for (const scenario of cases) test(`prepaid Sandbox | ${scenario.person} | ${scenario.campus}`, async ({ page }, info) => {
  const clientId = process.env.PAYPAL_SANDBOX_CLIENT_ID;
  const secret = process.env.PAYPAL_SANDBOX_CLIENT_SECRET;
  const merchant = process.env.PAYPAL_SANDBOX_MERCHANT_ID;
  if (!clientId || !secret || !merchant) throw new Error('Sandbox credentials and expected seller are required.');
  mkdirSync(evidenceDir, { recursive: true });
  const provider = await api.newContext({ baseURL: 'https://api-m.sandbox.paypal.com' });
  try {
    const oauth = await safeCall('Sandbox OAuth', () => provider.post('/v1/oauth2/token', {
      headers: { Authorization: `Basic ${Buffer.from(`${clientId}:${secret}`).toString('base64')}` }, form: { grant_type: 'client_credentials' }
    }));
    expect(oauth.status()).toBe(200);
    const providerHeaders = { Authorization: `Bearer ${(await oauth.json()).access_token}` };
    await page.goto('/');
    await page.getByRole('button', { name: scenario.person, exact: true }).click();
    const progressFile = path.join(evidenceDir, `${scenario.person.toLowerCase()}.json`);
    const prior = existsSync(progressFile) ? JSON.parse(readFileSync(progressFile, 'utf8')) : null;
    if (prior?.stage === 'verified-one-capture-two-pdfs') throw new Error('This completed Sandbox purchase already has saved proof. Verify it read-only instead of creating another payment.');
    const today = prior?.dates?.[0] ?? new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
    const anchor = new Date(`${today}T12:00:00Z`);
    const second = prior?.dates?.[1] ?? new Date(anchor.getTime() + 86400000).toISOString().slice(0, 10);
    if (prior?.purchaseId) {
      await page.goto(`/Prepaid/Review/${prior.purchaseId}`);
    } else {
    const plate = 'BP' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
    await page.goto('/Vehicles/Create');
    await page.getByLabel('License plate', { exact: true }).fill(plate);
    await page.getByLabel('Make', { exact: true }).selectOption('Honda');
    await page.getByLabel('Model', { exact: true }).selectOption('Civic');
    await page.getByRole('button', { name: 'Save vehicle', exact: true }).click();
    await page.goto('/Planner');
    await page.getByLabel('Your college', { exact: true }).selectOption(scenario.campus);
    await page.getByRole('radio', { name: 'Repeating visits', exact: true }).check();
    await page.getByLabel('Start of your planning period', { exact: true }).fill(today);
    await page.getByLabel('End date', { exact: true }).fill(second);
    await page.getByLabel('Fall', { exact: true }).check();
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    for (let index = 0; index < 7; index++) await page.getByLabel(days[index], { exact: true }).setChecked([anchor.getUTCDay(), (anchor.getUTCDay() + 1) % 7].includes(index));
    await page.getByRole('button', { name: 'Compare my options', exact: true }).click();
    await page.getByRole('button', { name: 'Buy these dates', exact: true }).click();
    const vehicle = page.getByLabel('Vehicle', { exact: true });
    await vehicle.selectOption((await vehicle.locator('option').filter({ hasText: plate }).getAttribute('value'))!);
    await page.getByRole('button', { name: 'Review these dates', exact: true }).click();
    }
    await expect(page.getByRole('heading', { name: 'Review your prepaid passes' })).toBeVisible();
    const panel = page.locator('#checkout-payment');
    const purchaseId = (await panel.getAttribute('data-application-id'))!;
    const reviewedSnapshot = await panel.getAttribute('data-reviewed-snapshot');
    const csrf = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
    const headers = { RequestVerificationToken: csrf };
    const amount = (scenario.unit * 2 / 100).toFixed(2);
    let orderId: string | null = null;
    let captureId: string | null = null;
    const save = (stage: string) => writeFileSync(path.join(evidenceDir, `${scenario.person.toLowerCase()}.json`), JSON.stringify({ stage, checkedAtUtc: new Date().toISOString(), ...scenario, purchaseId, orderId, captureId, dates: [today, second], amount, currency: 'USD', walletUiApprovalVerified: false }, null, 2));
    save('reviewed');
    const start = await page.request.post('/api/prepaid/orders', { headers, data: { purchaseId, reviewedSnapshot } });
    expect(start.status()).toBe(200); orderId = (await start.json()).id; expect(orderId).toBeTruthy(); if (prior?.orderId) expect(orderId).toBe(prior.orderId); save('order-created');
    const repeated = await page.request.post('/api/prepaid/orders', { headers, data: { purchaseId, reviewedSnapshot } });
    expect(repeated.status()).toBe(200); expect((await repeated.json()).id).toBe(orderId);
    const before = await safeCall('Sandbox order verification', () => provider.get(`/v2/checkout/orders/${orderId}`, { headers: providerHeaders }));
    expect(before.status()).toBe(200); const facts = await before.json();
    expect(facts.purchase_units).toHaveLength(1);
    expect(facts.purchase_units[0].amount).toMatchObject({ value: amount, currency_code: 'USD' });
    expect(facts.purchase_units[0].payee.merchant_id).toBe(merchant);
    const approved = await safeCall('Sandbox card confirmation', () => provider.post(`/v2/checkout/orders/${orderId}/confirm-payment-source`, {
      headers: { ...providerHeaders, 'PayPal-Request-Id': `solvia-prepaid-test-${orderId}` }, data: { payment_source: { card: {
        number: '4012888888881881', expiry: '2030-12', security_code: '123', name: `${scenario.person} Sandbox`,
        billing_address: { address_line_1: '123 Test Street', admin_area_1: 'CA', admin_area_2: 'San Jose', postal_code: '95131', country_code: 'US' }
      } } }
    }));
    expect(approved.ok()).toBe(true); expect((await approved.json()).status).toBe('APPROVED'); save('approved');
    const captured = await page.request.post('/api/prepaid/capture', { headers, data: { orderId } });
    expect(captured.status()).toBe(200); const result = await captured.json();
    expect(result.completed).toBe(true); expect(result.status).toBe('COMPLETED'); captureId = result.captureId; save('captured');
    const retried = await page.request.post('/api/prepaid/capture', { headers, data: { orderId } });
    expect(retried.status()).toBe(200); expect(await retried.json()).toMatchObject({ completed: true, captureId });
    const finalLookup = await safeCall('Sandbox capture verification', () => provider.get(`/v2/checkout/orders/${orderId}`, { headers: providerHeaders }));
    expect(finalLookup.status()).toBe(200); const final = await finalLookup.json();
    expect(final.status).toBe('COMPLETED'); expect(final.purchase_units[0].payments.captures).toHaveLength(1);
    expect(final.purchase_units[0].payments.captures[0]).toMatchObject({ id: captureId, status: 'COMPLETED', amount: { value: amount, currency_code: 'USD' } });
    await page.goto(`/Prepaid/Receipt/${purchaseId}`);
    await expect(page.locator('main')).toContainText(orderId!); await expect(page.locator('main')).toContainText(captureId!);
    await expect(page.locator('main')).toContainText('Upcoming'); await expect(page.locator('main')).toContainText('Valid today');
    const links = page.getByRole('link', { name: "Download this day's PDF", exact: true });
    await expect(links).toHaveCount(2);
    for (let index = 0; index < 2; index++) {
      const document = await page.request.get((await links.nth(index).getAttribute('href'))!);
      expect(document.status()).toBe(200); expect(document.headers()['content-type']).toContain('application/pdf');
      writeFileSync(path.join(evidenceDir, `${scenario.person.toLowerCase()}-${index + 1}.pdf`), await document.body());
    }
    const packet = await page.request.get(`/Prepaid/Pdf/${purchaseId}`); expect(packet.status()).toBe(200);
    writeFileSync(path.join(evidenceDir, `${scenario.person.toLowerCase()}-packet.pdf`), await packet.body());
    await page.screenshot({ path: path.join(evidenceDir, `${scenario.person.toLowerCase()}-receipt.png`), fullPage: true });
    save('verified-one-capture-two-pdfs');
    await info.attach('sanitized-prepaid-proof', { body: JSON.stringify({ purchaseId, orderId, captureId, amount, dates: [today, second], captureCount: 1, method: 'Official Sandbox test-card confirmation and Solvia REST capture; wallet popup not exercised' }), contentType: 'application/json' });
  } finally { await provider.dispose(); }
});
