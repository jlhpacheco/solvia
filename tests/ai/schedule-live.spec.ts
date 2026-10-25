import { test, expect } from '@playwright/test';

test.skip(process.env.SOLVIA_AI_PROVIDER !== 'together', 'This schedule adapter uses the configured Together service.');

test('real Together relative-date list includes today tomorrow and Wednesday', async ({ page }, info) => {
  const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles',
    year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  const before = today();
  const sentence = 'I want a parking permit today and tomorrow as well on wednesday. please. this is going to be a big week for me';
  await page.goto('/Planner');
  await page.getByLabel('Describe your visits', { exact: false }).fill(sentence);
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  const reader = page.getByTestId('schedule-reader');
  const source = await reader.getAttribute('data-source');
  const note = await reader.innerText();
  const anchor = note.match(/Using Pacific date (\d{4}-\d{2}-\d{2})/i)?.[1];
  expect(source, 'A built-in fallback is not evidence of a successful hosted-model read.').toBe('together');
  expect([before, today()]).toContain(anchor);
  const date = new Date(`${anchor}T00:00:00Z`);
  const plus = (days: number) => new Date(date.getTime() + days * 86_400_000).toISOString().slice(0, 10);
  const dates = [...new Set([plus(0), plus(1), plus((3 - date.getUTCDay() + 7) % 7)])].sort();
  const names = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  const expectedDays = [...new Set(dates.map(value => new Date(`${value}T00:00:00Z`).getUTCDay()))]
    .sort().map(value => names[value]);
  const checked = await page.locator('input[name="Weekdays"]:checked').evaluateAll(inputs =>
    inputs.map(input => (input as HTMLInputElement).value));
  expect(checked.slice().sort()).toEqual(expectedDays.slice().sort());
  await expect(page.getByLabel('Start of your planning period', { exact: true })).toHaveValue(dates[0]);
  await expect(page.getByLabel('End date', { exact: true })).toHaveValue(dates.at(-1)!);
  for (const value of dates) expect(note).toContain(value);
  await expect(reader).not.toContainText('needs your review');
  await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(sentence);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  await info.attach('actual-relative-date-read', { body: Buffer.from(JSON.stringify({
    checkedAtUtc: new Date().toISOString(), source, anchor, dates, checked
  })), contentType: 'application/json' });
  await info.attach('relative-date-screen', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
});

for (const example of [
  { label: 'annotated three-visit range', sentence: '10/26/2026 , monday to 10/30/2026, Friday. for 3 visits, as well as Wednesday', end: '2026-10-30' },
  { label: 'misspelled weekdays and month', sentence: 'Monay, Wendesday and Firday from 10/26/2026 until the end of Novemeber 2026', end: '2026-11-30' }
]) {
  test(`real Together natural schedule: ${example.label}`, async ({ page }, info) => {
    await page.goto('/Planner');
    await page.getByLabel('Describe your visits', { exact: false }).fill(example.sentence);
    const started = Date.now();
    await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
    const reader = page.getByTestId('schedule-reader');
    const source = await reader.getAttribute('data-source');
    const start = await page.getByLabel('Start of your planning period', { exact: true }).inputValue();
    const end = await page.getByLabel('End date', { exact: true }).inputValue();
    const days = await page.locator('input[name="Weekdays"]:checked').evaluateAll(inputs =>
      inputs.map(input => (input as HTMLInputElement).value));
    await info.attach('actual-natural-schedule-reader', {
      body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source, start, end, days,
        elapsedMs: Date.now() - started, label: example.label })), contentType: 'application/json'
    });
    expect(source, 'Fallback is not a successful hosted-model extraction.').toBe('together');
    expect(start).toBe('2026-10-26');
    expect(end).toBe(example.end);
    expect(days).toEqual(['Monday', 'Wednesday', 'Friday']);
    await expect(reader).not.toContainText('needs your review');
    await expect(page.getByLabel('Describe your visits', { exact: false })).toHaveValue(example.sentence);
    await expect(page.getByTestId('plan-result')).toHaveCount(0);
    await expect(page.locator('#Weeks')).toHaveCount(0);
    await info.attach('natural-schedule-screen', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  });
}

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

test('real Together month-end sentence keeps October 26 through November 30 and MWF', async ({ page }, info) => {
  const started = Date.now();
  await page.goto('/Planner');
  await page.getByLabel('Describe your visits', { exact: false }).fill(
    'i am coming over monday 10/26/2026 until the end of november 2026, every monday , Wednesday and Friday. ok');
  await page.getByRole('button', { name: 'Read my schedule', exact: true }).click();
  const source = await page.getByTestId('schedule-reader').getAttribute('data-source');
  const startDate = await page.getByLabel('Start of your planning period', { exact: true }).inputValue();
  const endDate = await page.getByLabel('End date', { exact: true }).inputValue();
  const checked = await page.locator('input[name="Weekdays"]:checked').evaluateAll(inputs =>
    inputs.map(input => (input as HTMLInputElement).value));
  await info.attach('actual-month-end-reader', {
    body: Buffer.from(JSON.stringify({ checkedAtUtc: new Date().toISOString(), source,
      startDate, endDate, checked, elapsedMs: Date.now() - started })), contentType: 'application/json'
  });
  expect(source, 'A rules fallback must not be called a successful hosted-model answer.').toBe('together');
  expect(startDate).toBe('2026-10-26');
  expect(endDate).toBe('2026-11-30');
  expect(checked).toEqual(['Monday', 'Wednesday', 'Friday']);
  await expect(page.getByLabel('Repeating visits', { exact: true })).toBeChecked();
  await expect(page.locator('#Weeks')).toHaveCount(0);
  await expect(page.getByTestId('plan-result')).toHaveCount(0);
  const status = await (await page.request.get('/api/ai/status')).json();
  expect(status.model).toBe('openai/gpt-oss-120b');
  expect(status.readers.find((reader: { reader: string }) => reader.reader === 'Visits').state).toBe('success');
  await info.attach('month-end-screen', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
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
