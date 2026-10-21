import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test.describe.configure({ timeout: 60_000 });
const campus = 'Fresno%20City%20College';
const newPlate = () => 'L' + randomUUID().replaceAll('-', '').slice(0, 7).toUpperCase();

async function readVehicle(page: Page, plate: string) {
  await page.goto(`/Permits/Questions?college=${campus}`);
  await page.getByLabel(/Your car/).fill(`blue Honda Civic, license plate ${plate}`);
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Did I get this right?', exact: true })).toBeVisible();
}

async function savedPermit(page: Page, person: string, plate: string) {
  await page.goto('/');
  await page.getByRole('button', { name: person, exact: true }).click();
  await readVehicle(page, plate);
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('heading', { name: person === 'Elena' ? /Permit live/ : 'Check your total' })).toBeVisible();
  return page.url();
}

async function garageEdit(page: Page, plate: string) {
  await page.goto('/Vehicles');
  await page.locator('.roster-card').filter({ hasText: plate }).getByRole('link', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Edit vehicle', exact: true })).toBeVisible();
}

test('vehicle purchase lock keeps issued vehicle facts when the garage attempts a color change', async ({ page }) => {
  const plate = newPlate();
  const receipt = await savedPermit(page, 'Elena', plate);
  await garageEdit(page, plate);
  await page.getByLabel('Color', { exact: true }).selectOption({ label: 'Red' });
  await page.getByRole('button', { name: 'Save vehicle', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: /payment or issued permit/ })).toBeVisible();
  // The rejected choice remains visible for correction, but was never saved.
  await expect(page.getByLabel('Color', { exact: true }).locator('option:checked')).toHaveText('Red');
  // Reopen with GET: refreshing an invalid POST resubmits its rejected form.
  await garageEdit(page, plate);
  await expect(page.getByLabel('Color', { exact: true }).locator('option:checked')).toHaveText('Blue');
  await page.goto(receipt);
  await expect(page.locator('main')).toContainText('Civic');
  await expect(page.locator('main')).toContainText('Blue');
});

test('vehicle purchase lock rejects a different description of an issued plate through a fresh AI draft', async ({ page }) => {
  const plate = newPlate();
  const receipt = await savedPermit(page, 'Elena', plate);
  await readVehicle(page, plate);
  await page.getByLabel('Model', { exact: true }).fill('Accord');
  await page.getByRole('button', { name: 'Yes — show my total', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: /payment or issued permit/ })).toBeVisible();
  await expect(page.getByLabel('Model', { exact: true })).toHaveValue('Accord');
  await page.goto(receipt);
  await expect(page.locator('main')).toContainText('Civic');
  await expect(page.locator('main')).not.toContainText('Accord');
});

test('vehicle purchase lock still permits garage corrections for an unbound pending application', async ({ page }) => {
  const plate = newPlate();
  const review = await savedPermit(page, 'Maria', plate);
  await garageEdit(page, plate);
  await page.getByLabel('Color', { exact: true }).selectOption({ label: 'Red' });
  await page.getByRole('button', { name: 'Save vehicle', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'My vehicles', exact: true })).toBeVisible();
  await garageEdit(page, plate);
  await expect(page.getByLabel('Color', { exact: true }).locator('option:checked')).toHaveText('Red');
  await page.goto(review);
  await expect(page.locator('main')).toContainText('Red');
  await expect(page.locator('.display-4 strong')).toHaveText('$30.00');
  await expect(page.locator('#paypal-order-id')).toHaveText('Not created yet — choose PayPal above.');
});
