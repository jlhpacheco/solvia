import { test, expect } from '@playwright/test';

test.skip(process.env.SOLVIA_AI_PROVIDER !== 'together', 'This schedule adapter uses the configured Together service.');

for (const example of [
  { sentence: 'Tuesdays and Thursdays for three weeks', days: ['Tuesday', 'Thursday'], weeks: '3' },
  { sentence: 'Monday, Wednesday and Friday for two weeks', days: ['Monday', 'Wednesday', 'Friday'], weeks: '2' },
  { sentence: 'Weekends for four weeks', days: ['Saturday', 'Sunday'], weeks: '4' }
]) {
  test(`real Together schedule draft: ${example.sentence}`, async ({ page }, info) => {
    await page.goto('/Planner');
    await expect(page.locator('#schedule-help')).toContainText("OpenAI's US-developed GPT-OSS 120B");
    await page.getByLabel('Describe your visits', { exact: false }).fill(example.sentence);
    const startDate = await page.locator('#StartDate').inputValue();
    const expectedEnd = new Date(`${startDate}T00:00:00Z`);
    expectedEnd.setUTCDate(expectedEnd.getUTCDate() + Number(example.weeks) * 7 - 1);
    const started = Date.now();
    await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
    const reader = page.getByTestId('schedule-reader');
    await expect(reader).toBeVisible();
    const source = await reader.getAttribute('data-source');
    const checked = await page.locator('input[name="Weekdays"]:checked').evaluateAll(inputs => inputs.map(input => (input as HTMLInputElement).value));
    const endDate = await page.getByLabel('End date', { exact: true }).inputValue();
    await info.attach('actual-schedule-reader', {
      body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, checked, startDate, endDate, elapsedMs: Date.now() - started, expected: example })),
      contentType: 'application/json'
    });
    expect(source, 'Fallback is not evidence that the schedule model ran successfully.').toBe('together');
    expect(checked.slice().sort()).toEqual(example.days.slice().sort());
    expect(endDate).toBe(expectedEnd.toISOString().slice(0, 10));
    await expect(page.locator('#Weeks')).toHaveCount(0);
    await expect(page.getByTestId('plan-result')).toHaveCount(0);
  });
}

test('real schedule reader leaves uncertainty for explicit review', async ({ page }, info) => {
  await page.goto('/Planner');
  await page.getByLabel('Describe your visits', { exact: false }).fill('Maybe Tuesdays or Thursdays. I am unsure how many weeks.');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  const source = await page.getByTestId('schedule-reader').getAttribute('data-source');
  expect(['together', 'fallback']).toContain(source);
  await expect(page.locator('input[name="Weekdays"]:checked')).toHaveCount(0);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue('');
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  await info.attach('actual-uncertain-schedule-reader', { body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, confirmedNoDefaults: true })), contentType: 'application/json' });
});

test('real Together one-time dated visit uses only a visit date and verifies the operational indicator', async ({ page }, info) => {
  await page.goto('/Planner');
  await page.getByLabel('Your college', { exact: true }).selectOption('CSU East Bay');
  await page.getByLabel('Start of your planning period').fill('2026-10-23');
  await page.getByLabel('End date', { exact: true }).fill('');
  await page.getByLabel('Describe your visits', { exact: false }).fill('One visit on 10/27/2026.');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  const source = await page.getByTestId('schedule-reader').getAttribute('data-source');
  expect(source, 'A rules result must not be called a successful hosted model response.').toBe('together');
  await expect(page.getByLabel('Visit date', { exact: true })).toHaveValue('2026-10-27');
  await expect(page.getByLabel('One visit', { exact: true })).toBeChecked();
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.locator('#repeat-end')).toBeHidden();
  await expect(page.locator('#EndDate')).toBeDisabled();
  await expect(page.locator('#repeat-weekdays')).toBeHidden();
  await expect(page.locator('#single-visit-date')).toContainText('Tuesday');
  const response = await page.request.get('/api/ai/status');
  expect(response.status()).toBe(200);
  const status = await response.json();
  expect(status.state).toBe('recent');
  expect(status.provider).toBe('Together AI');
  expect(status.model).toBe('openai/gpt-oss-120b');
  expect(status.lastSuccessAtUtc).toBeTruthy();
  expect(status.readers.find((reader: { reader: string }) => reader.reader === 'Visits').state).toBe('success');
  await expect(page.getByTestId('ai-operation-status')).toHaveAttribute('data-state', 'recent');
  await info.attach('single-visit-ai-status-screen', {
    body: await page.screenshot({ fullPage: true }), contentType: 'image/png'
  });
  await info.attach('actual-one-time-schedule-and-ai-status', {
    body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, date: '2026-10-27', singleVisit: true, repeatFieldsVisible: false, status })),
    contentType: 'application/json'
  });
});
