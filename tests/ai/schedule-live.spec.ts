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
    const started = Date.now();
    await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
    const reader = page.getByTestId('schedule-reader');
    await expect(reader).toBeVisible();
    const source = await reader.getAttribute('data-source');
    const checked = await page.locator('input[name="Weekdays"]:checked').evaluateAll(inputs => inputs.map(input => (input as HTMLInputElement).value));
    const weeks = await page.getByLabel('Number of weeks', { exact: true }).inputValue();
    await info.attach('actual-schedule-reader', {
      body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, checked, weeks, elapsedMs: Date.now() - started, expected: example })),
      contentType: 'application/json'
    });
    expect(source, 'Fallback is not evidence that the schedule model ran successfully.').toBe('together');
    expect(checked.slice().sort()).toEqual(example.days.slice().sort());
    expect(weeks).toBe(example.weeks);
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
  await expect(page.getByLabel('Number of weeks', { exact: true })).toHaveValue('');
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  await info.attach('actual-uncertain-schedule-reader', { body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, confirmedNoDefaults: true })), contentType: 'application/json' });
});
