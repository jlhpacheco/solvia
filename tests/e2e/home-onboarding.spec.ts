import { test, expect } from '@playwright/test';

test('@smoke home puts fictional-person selection before application choices and shows resolved roles', async ({ page }) => {
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
  expect(first!.y + first!.height).toBeLessThanOrEqual(second!.y);
  await expect(choices.getByRole('link', { name: 'Ask Solvia', exact: true })).toHaveAttribute('href', '/Permits/Ask');
  await expect(choices.getByRole('link', { name: 'Use the classic form', exact: true })).toHaveAttribute('href', '/Permits/Apply');
  await expect(choices.getByRole('link', { name: 'Plan my parking', exact: true })).toHaveAttribute('href', '/Planner');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
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
