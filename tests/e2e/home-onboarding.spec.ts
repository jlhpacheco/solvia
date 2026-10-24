import { test, expect } from '@playwright/test';

let browserErrors: string[];
let paymentRequests: string[];
test.beforeEach(({ page }) => {
  browserErrors = [];
  paymentRequests = [];
  page.on('pageerror', error => browserErrors.push(error.message));
  page.on('request', request => {
    if (request.method() === 'POST' && /\/api\/paypal\/(orders|capture)|\/Checkout\/Start\//.test(request.url())) paymentRequests.push(request.url());
  });
});
test.afterEach(() => {
  expect(browserErrors).toEqual([]);
  expect(paymentRequests, 'Home navigation must not start a payment').toEqual([]);
});

test('@smoke home puts fictional-person selection before application choices and shows resolved roles', async ({ page }, info) => {
  await page.goto('/');
  const people = page.getByTestId('demo-step-one');
  const choices = page.getByTestId('demo-step-two');
  await expect(people).toContainText('STEP 1');
  await expect(choices).toContainText('STEP 2');
  await expect(page.getByRole('heading', { name: 'Welcome, Maria.', exact: true })).toBeVisible();
  await expect(people.getByRole('button', { name: 'Maria', exact: true })).toHaveAttribute('aria-pressed', 'true');
  for (const [person, role] of [['Maria', 'Student'], ['Elena', 'Employee'], ['Pat', 'Visitor (public)']]) {
    const button = people.getByRole('button', { name: person, exact: true });
    await expect(button).toBeVisible();
    const descriptionId = await button.getAttribute('aria-describedby');
    await expect(page.locator(`[id="${descriptionId}"]`)).toHaveText(role);
  }
  expect(await people.evaluate(node => !!(node.compareDocumentPosition(document.querySelector('[data-testid="demo-step-two"]')!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
  const first = await people.boundingBox();
  const second = await choices.boundingBox();
  if (page.viewportSize()!.width >= 1180) {
    expect(first!.x + first!.width).toBeLessThanOrEqual(second!.x);
    const actions = await choices.locator('.sv-start-actions').boundingBox();
    expect(actions!.y + actions!.height, 'All three desktop actions fit in the first screen').toBeLessThanOrEqual(page.viewportSize()!.height);
  } else {
    expect(first!.y + first!.height).toBeLessThanOrEqual(second!.y);
  }
  await expect(choices.getByRole('link', { name: 'Ask Solvia', exact: true })).toHaveAttribute('href', '/Permits/Ask');
  await expect(choices.getByRole('link', { name: 'Use the classic form', exact: true })).toHaveAttribute('href', '/Permits/Apply');
  await expect(choices.getByRole('link', { name: 'Plan my parking', exact: true })).toHaveAttribute('href', '/Planner');
  for (const control of await people.getByRole('button').all()) {
    const box = await control.boundingBox();
    expect(box!.width).toBeGreaterThanOrEqual(44);
    expect(box!.height).toBeGreaterThanOrEqual(44);
    expect(await control.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  }
  for (const control of await choices.getByRole('link').all()) {
    const box = await control.boundingBox();
    expect(box!.width).toBeGreaterThanOrEqual(44);
    expect(box!.height).toBeGreaterThanOrEqual(44);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  const guide = await page.getByRole('region', { name: 'Your path to a parking permit' }).boundingBox();
  if (page.viewportSize()!.width < 760 || page.viewportSize()!.height < 600) {
    expect(second!.y + second!.height, 'Phone choices come before explanatory content').toBeLessThanOrEqual(guide!.y);
  }
  await page.screenshot({ path: info.outputPath('home-layout.png'), fullPage: true, scale: 'css' });
});

test('planning and the compact resource links reach their real pages', async ({ page, request }) => {
  await page.goto('/');
  const links = page.getByRole('navigation', { name: 'Explore Solvia' }).getByRole('link');
  await expect(links).toHaveCount(3);
  const destinations = await links.evaluateAll(nodes => nodes.map(node => (node as HTMLAnchorElement).pathname));
  expect(destinations).toEqual(['/College/Fees', '/Permits/My', '/Home/Privacy']);
  for (const destination of destinations) expect((await request.get(destination)).status()).toBe(200);
  await page.getByTestId('demo-step-two').getByRole('link', { name: 'Plan my parking', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Plan your parking.', exact: true })).toBeVisible();
});

test('AI status remains readable and can be opened and closed within the viewport', async ({ page }) => {
  await page.goto('/');
  const status = page.getByTestId('ai-operation-status');
  const summary = status.locator('summary');
  await summary.click();
  await expect(status).toHaveAttribute('open', '');
  const panel = status.locator('.sv-ai-status-panel');
  await expect(panel).toBeVisible();
  const box = await panel.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(-1);
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
  await expect(panel).toContainText('Checking status never calls a model');
  await summary.click();
  await expect(status).not.toHaveAttribute('open', '');
  await expect(page.getByTestId('demo-step-one').getByRole('button', { name: 'Maria', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false });
  test('native keyboard selection and application links still work', async ({ page }) => {
    await page.goto('/');
    const person = page.getByRole('button', { name: 'Pat', exact: true });
    await person.focus();
    await person.press('Enter');
    await expect(page.getByRole('heading', { name: 'Welcome, Pat.', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Pat', exact: true })).toHaveAttribute('aria-pressed', 'true');
    const action = page.getByTestId('demo-step-two').getByRole('link', { name: 'Ask Solvia', exact: true });
    await action.focus();
    await action.press('Enter');
    await expect(page.getByRole('heading', { name: 'Pick your college', exact: true })).toBeVisible();
    await expect(page.locator('main')).toContainText('you are Pat');
  });
});

test('selected demo person is retained through Ask Solvia and the classic entry', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Pat', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome, Pat.', exact: true })).toBeVisible();
  await expect(page.getByTestId('demo-step-two')).toContainText('Continue as Pat · Visitor (public)');
  await page.getByTestId('demo-step-two').getByRole('link', { name: 'Ask Solvia', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pick your college', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('you are Pat');
  await page.goto('/');
  await page.getByRole('button', { name: 'Elena', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome, Elena.', exact: true })).toBeVisible();
  await expect(page.getByTestId('demo-step-two')).toContainText('Continue as Elena · Employee');
  await page.getByTestId('demo-step-two').getByRole('link', { name: 'Use the classic form', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Apply for a permit', exact: true })).toBeVisible();
  await expect(page.locator('main')).toContainText('Demo person: Elena — Employee');
});
