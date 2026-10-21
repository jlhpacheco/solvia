import { test, expect, request as api } from '@playwright/test';
import { randomUUID } from 'node:crypto';

// Real SDK rendering and progressive enhancement; never clicks a payment
// option, creates a PayPal order, or confirms/captures a transaction.
test.skip(process.env.SOLVIA_NATIVE_SMOKE !== '1', 'Opt in to the live SDK rendering smoke check.');
test.setTimeout(60_000);
let applicationId: string;

function token(html: string): string {
  const value = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
  if (!value) throw new Error('The application did not return its form token.');
  return value;
}

function flow(html: string): string {
  const value = html.match(/name="(?:flowId|FlowId)"[^>]*value="([^"]+)"/)?.[1];
  if (!value) throw new Error('The actual flow form did not return its tab identity.');
  return value;
}

test.beforeAll(async () => {
  const request = await api.newContext({ baseURL: 'http://localhost:5156' });
  try {
    const questions = await request.get('/Permits/Questions?college=Fresno%20City%20College');
    expect(questions.status()).toBe(200);
    const plate = 'UI' + randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase();
    const words = `blue Honda Civic, license plate number ${plate}`;
    const questionsHtml = await questions.text();
    const understood = await request.post('/Permits/Understand', { form: {
      __RequestVerificationToken: token(questionsHtml), flowId: flow(questionsHtml), college: 'Fresno City College',
      carText: words, permitType: 'Term', term: 'Fall'
    } });
    expect(understood.status()).toBe(200);
    const understoodHtml = await understood.text();
    const draft = await request.post('/Permits/Review', { maxRedirects: 0, form: {
      __RequestVerificationToken: token(understoodHtml), FlowId: flow(understoodHtml), College: 'Fresno City College',
      CarText: words, Plate: plate, CarMake: 'Honda', CarModel: 'Civic', CarColor: 'Blue',
      CarState: 'CA', PermitType: 'Term', Term: 'Fall', Source: 'fallback'
    } });
    expect(draft.status()).toBe(302);
    const id = draft.headers().location?.match(/[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}/i)?.[0];
    if (!id) throw new Error('The unpaid application did not return its ID.');
    applicationId = id;
  } finally { await request.dispose(); }
});

test('@smoke actual PayPal SDK exposes PayPal and card without a duplicate CTA', async ({ page }) => {
  let ordersCreated = 0;
  page.on('request', request => { if (request.url().endsWith('/api/paypal/orders')) ordersCreated++; });
  await page.goto(`/Permits/Review/${applicationId}`);
  await expect(page.getByRole('heading', { name: 'Check your total', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('$30.00');
  const provider = page.frameLocator('#paypal-button-container iframe').first();
  await expect(provider.getByRole('link', { name: 'PayPal', exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(provider.getByRole('link', { name: 'Debit or Credit Card', exact: true })).toBeVisible();
  await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
  await expect(page.getByRole('button', { name: /^Pay \$/ })).toHaveCount(0);
  await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
  expect(ordersCreated, 'Displaying options must not create a payment order').toBe(0);
});

test('@smoke JavaScript-disabled checkout has one protected PayPal continuation', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL: 'http://localhost:5156' });
  try {
    const page = await context.newPage();
    for (const surface of ['Permits/Review', 'Checkout']) {
      await page.goto(`/${surface}/${applicationId}`);
      await expect(page.getByRole('button', { name: 'Continue to PayPal', exact: true })).toBeVisible();
      await expect(page.locator('#paypal-redirect-fallback')).toBeHidden();
      const form = page.locator('form').filter({ has: page.getByRole('button', { name: 'Continue to PayPal', exact: true }) }).filter({ visible: true });
      await expect(form).toHaveAttribute('action', `/Checkout/Start/${applicationId}`);
      expect(await form.locator('input[name="__RequestVerificationToken"]').inputValue()).not.toBe('');
      expect(await form.locator('input[name="reviewedSnapshot"]').inputValue()).toMatch(/^[a-f0-9]{64}$/);
      await expect(page.locator('#paypal-order-id')).toContainText('Not created yet');
    }
  } finally { await context.close(); }
});
