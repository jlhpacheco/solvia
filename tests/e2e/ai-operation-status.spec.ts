import { test, expect } from '@playwright/test';

const successAt = '2026-10-23T12:00:00.000Z';
const snapshot = (state: string, label: string) => ({ state, label,
  description: state === 'recent' ? 'A validated response was observed. Availability may change.' : 'The latest read used built-in rules.',
  provider: 'Together AI', model: 'openai/gpt-oss-120b', observedAtUtc: '2026-10-23T12:01:00Z',
  lastSuccessAtUtc: state === 'unchecked' ? null : successAt, lastAttemptAtUtc: successAt, readers: [] });

test('@smoke AI status starts from observed state and preserves fallback versus earlier success', async ({ page }) => {
  let current = snapshot('unchecked', 'AI configured · not checked');
  let gets = 0;
  await page.route('**/api/ai/status', route => { gets++; return route.fulfill({ json: current }); });
  await page.goto('/');
  const status = page.locator('#ai-operation-status');
  await expect(status).toHaveAttribute('data-state', 'unchecked');
  await status.locator('summary').click();
  await expect(status.locator('[data-ai-success]')).toHaveText('None observed');
  current = snapshot('recent', 'AI: response verified');
  await status.getByRole('button', { name: 'Refresh status', exact: true }).click();
  await expect(status).toHaveAttribute('data-state', 'recent');
  await expect(status.locator('[data-ai-success]')).toHaveAttribute('datetime', successAt);
  await expect(status.locator('[data-ai-model]')).toContainText('openai/gpt-oss-120b');
  current = snapshot('fallback', 'AI: latest read used rules');
  await status.getByRole('button', { name: 'Refresh status', exact: true }).click();
  await expect(status).toHaveAttribute('data-state', 'fallback');
  await expect(status.locator('[data-ai-success]')).toHaveAttribute('datetime', successAt);
  await expect(page.locator('#ai-status-announcement')).toHaveText('AI: latest read used rules');
  expect(gets).toBeLessThanOrEqual(4);
});

test('AI status network failure removes the recent-success indicator without erasing historical time', async ({ page }) => {
  let fail = false;
  await page.route('**/api/ai/status', route => fail ? route.abort() : route.fulfill({ json: snapshot('recent', 'AI: response verified') }));
  await page.goto('/');
  const status = page.locator('#ai-operation-status');
  await expect(status).toHaveAttribute('data-state', 'recent');
  await status.locator('summary').click();
  fail = true;
  await status.getByRole('button', { name: 'Refresh status', exact: true }).click();
  await expect(status).toHaveAttribute('data-state', 'unavailable');
  await expect(status.locator('[data-ai-label]')).toHaveText('AI status unavailable');
  await expect(status.locator('[data-ai-success]')).toHaveAttribute('datetime', successAt);
});

test('AI status remains meaningful without JavaScript and makes no model health call', async ({ browser, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL, viewport, isMobile, hasTouch, deviceScaleFactor });
  try {
    const page = await context.newPage();
    const inference: string[] = [];
    page.on('request', request => { if (/api\/chat|api\.together|11434/.test(request.url())) inference.push(request.url()); });
    await page.goto('/');
    const status = page.locator('#ai-operation-status');
    await expect(status).toBeVisible();
    await expect(status).not.toHaveAttribute('data-state', 'recent');
    await status.locator('summary').click();
    await expect(status.locator('[data-ai-model]')).toContainText('Ollama');
    await expect(status).toContainText('Checking status never calls a model');
    await expect(status.locator('[data-ai-refresh]')).toBeHidden();
    expect(inference).toEqual([]);
  } finally { await context.close(); }
});

test('AI status resumes gentle refresh after a fast back-forward page restore', async ({ page }) => {
  const frozen = new Date('2026-10-23T12:01:00Z');
  await page.clock.install({ time: frozen });
  await page.clock.pauseAt(frozen);
  let current = snapshot('recent', 'AI: response verified');
  let requests = 0;
  await page.route('**/api/ai/status', route => { requests++; return route.fulfill({ json: current }); });
  await page.goto('/');
  const status = page.locator('#ai-operation-status');
  await expect(status).toHaveAttribute('data-state', 'recent');
  // Deterministic lifecycle-event emulation, not a claim of a real Safari
  // back-forward cache test: less than1s has elapsed on the page clock.
  await page.evaluate(() => {
    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
  });
  expect(requests).toBe(1);
  current = snapshot('stale', 'AI: earlier response');
  await page.clock.fastForward(61_000);
  await expect(status).toHaveAttribute('data-state', 'stale');
  expect(requests).toBeLessThanOrEqual(3);
  await expect(status.locator('[data-ai-success]')).toHaveAttribute('datetime', successAt);
});

for (const scenario of [
  { name: 'vehicle form action', page: '/Permits/Questions?college=Fresno%20City%20College',
    input: '#carText', text: 'Fictional white Volvo 760 plate 654321', formId: 'vehicle-questions',
    button: 'Read my answers', post: '/Permits/Understand', explicitAction: false },
  { name: 'planner explicit button action', page: '/Planner',
    input: '#ScheduleText', text: 'Tuesdays and Thursdays for three weeks', formId: 'planner-form',
    button: 'Read my schedule', post: '/Planner/Read', explicitAction: true }
]) {
test(`AI status shows draft reading during the ${scenario.name}`, async ({ page }) => {
  await page.route('**/api/ai/status', route => route.fulfill({ json: snapshot('unchecked', 'AI configured · not checked') }));
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  let posted = false;
  await page.route('**' + scenario.post, async route => { posted = true; await held; await route.abort(); });
  await page.goto(scenario.page);
  await expect(page.locator('#ai-operation-status')).toHaveAttribute('data-state', 'unchecked');
  // The controlled server uses Ollama. This distinct mocked public name proves
  // the initial read-only status refresh finished before starting the submit.
  await expect(page.locator('#ai-operation-status [data-ai-model]')).toContainText('Together AI');
  await page.locator(scenario.input).fill(scenario.text);
  const read = page.getByRole('button', { name: scenario.button, exact: true });
  // These two real markup contracts deliberately exercise different paths.
  // HTMLButtonElement.formAction alone also has a value without an override.
  if (scenario.explicitAction) await expect(read).toHaveAttribute('formaction', scenario.post);
  else await expect(read).not.toHaveAttribute('formaction');
  let observed: unknown;
  page.on('console', message => {
    const prefix = 'SOLVIA_AI_HEADER_SUBMIT:';
    if (message.text().startsWith(prefix)) observed = JSON.parse(message.text().slice(prefix.length));
  });
  // Register after the app's document listener. Observe its actual result;
  // do not alter the handler or prevent the normal form navigation.
  await page.evaluate(formId => {
    document.addEventListener('submit', event => {
      if (!(event.target instanceof HTMLFormElement) || event.target.id !== formId) return;
      const status = document.getElementById('ai-operation-status');
      console.debug('SOLVIA_AI_HEADER_SUBMIT:' + JSON.stringify({
        state: status?.dataset.state,
        label: status?.querySelector('[data-ai-label]')?.textContent?.trim(),
        explicitAction: event.submitter?.hasAttribute('formaction') ?? false
      }));
    });
  }, scenario.formId);
  try {
    await read.click({ noWaitAfter: true });
    // Protocol-side observations do not wait for the deliberately held POST.
    await expect.poll(() => observed).toEqual({ state: 'reading', label: 'Reading your draft…', explicitAction: scenario.explicitAction });
    await expect.poll(() => posted).toBe(true);
  } finally { release(); }
});
}
