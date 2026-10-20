import { test, expect } from '@playwright/test';

test('@smoke vehicle reader announces pending work and preserves the protected form values', async ({ page }) => {
  test.setTimeout(60_000);
  await page.goto('/Permits/Questions?college=CSU%20Monterey%20Bay');
  await page.getByLabel('2 · Your car', { exact: false }).fill('white Volvo 760, license plate 234221');
  await page.locator('label[for="termSummer"]').click();
  await page.locator('label[for="typeDaily"]').click();
  await expect(page.locator('#carText')).toHaveAttribute('maxlength', '1000');

  // Observe the submit state after the app's handler without changing or
  // preventing the browser's normal form navigation.
  let pendingState: unknown;
  page.on('console', message => {
    const prefix = 'SOLVIA_PENDING_STATE:';
    if (message.text().startsWith(prefix)) {
      pendingState = JSON.parse(message.text().slice(prefix.length));
    }
  });
  await page.evaluate(() => {
    document.querySelector('#vehicle-questions')!.addEventListener('submit', () => {
      const button = document.querySelector<HTMLButtonElement>('#read-answers');
      const status = document.querySelector<HTMLElement>('#ai-reading-status');
      const progress = document.querySelector<HTMLElement>('progress');
      console.debug('SOLVIA_PENDING_STATE:' + JSON.stringify({
        disabled: button?.disabled,
        label: button?.textContent?.trim(),
        status: status?.textContent?.trim(),
        statusVisible: !!status && !status.hidden && status.getClientRects().length > 0,
        progressVisible: !!progress && progress.getClientRects().length > 0,
        busy: document.querySelector('#vehicle-questions')?.getAttribute('aria-busy'),
      }));
    });
  });

  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let posted: URLSearchParams | undefined;
  await page.route('**/Permits/Understand', async route => {
    posted = new URLSearchParams(route.request().postData() ?? '');
    await gate;
    await route.continue();
  });
  try {
    await page.getByRole('button', { name: 'Read my answers', exact: true }).click({ noWaitAfter: true });
    // Protocol observations avoid waiting for the intentionally held navigation.
    await expect.poll(() => pendingState).toMatchObject({
      disabled: true, label: 'Reading your answers…',
      status: expect.stringContaining('Reading your vehicle details'),
      statusVisible: true, progressVisible: true, busy: 'true',
    });
    await expect.poll(() => posted?.get('college')).toBe('CSU Monterey Bay');
    expect(posted?.get('permitType')).toBe('Daily');
    expect(posted?.get('term')).toBe('Summer');
    expect(posted?.get('__RequestVerificationToken')).toBeTruthy();
  } finally { release(); }

  await expect(page.getByRole('heading', { name: 'Did I get this right?' })).toBeVisible();
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue('234221');
  await expect(page.locator('input[name="PermitType"]')).toHaveValue('Daily');
  await expect(page.locator('input[name="Term"]')).toHaveValue('Summer');
  await page.goBack();
  await expect(page.getByRole('button', { name: 'Read my answers', exact: true })).toBeEnabled();
  await expect(page.locator('#ai-reading-status')).toBeHidden();
});

test('@smoke day passes hide semester choices and preserve the term selection through review', async ({ page }) => {
  test.setTimeout(60_000);
  await page.goto('/Permits/Questions?college=CSU%20Monterey%20Bay');
  const termOptions = page.locator('#term-options');
  const semesterGroup = page.getByRole('radiogroup', { name: 'Term', exact: true });

  await expect(semesterGroup).toBeVisible();
  await page.locator('label[for="termSummer"]').click();
  await page.locator('label[for="typeDaily"]').click();
  await expect(termOptions).toBeHidden();
  await expect(semesterGroup).toHaveCount(0);
  await expect(page.locator('#termSummer')).toBeChecked();
  await expect(page.locator('#termSummer')).toBeEnabled();

  await page.locator('label[for="typeTerm"]').click();
  await expect(semesterGroup).toBeVisible();
  await expect(page.locator('#termSummer')).toBeChecked();
  await page.locator('label[for="typeDaily"]').click();
  await page.getByLabel('2 · Your car', { exact: false }).fill('white Volvo 760, license plate DAY654321');
  await page.getByRole('button', { name: 'Read my answers', exact: true }).click();

  await expect(page.getByRole('heading', { name: 'Did I get this right?' })).toBeVisible();
  const summary = page.locator('form[action="/Permits/Review"] > p');
  await expect(summary).toContainText('Permit: Day pass');
  await expect(summary).not.toContainText('Summer');
  await expect(page.getByLabel('License plate', { exact: true })).toHaveValue('DAY654321');
  await expect(page.locator('input[name="PermitType"]')).toHaveValue('Daily');
  await expect(page.locator('input[name="Term"]')).toHaveValue('Summer');
  await page.getByRole('button', { name: 'Yes — show my total' }).click();

  await expect(page.getByRole('heading', { name: 'Check your total' })).toBeVisible();
  const permit = page.locator('dt').filter({ hasText: /^Permit$/ }).locator('xpath=following-sibling::dd[1]');
  await expect(permit).toHaveText('Daily permit');
  await expect(page.locator('.display-4 strong')).toHaveText('$5.00');
  const dates = page.locator('dt').filter({ hasText: /^Dates$/ }).locator('xpath=following-sibling::dd[1]');
  const [start, end] = (await dates.innerText()).split('→').map(value => value.trim());
  expect(start).toBeTruthy();
  expect(end).toBe(start);
  await expect(page.locator('input[name="PermitType"]')).toHaveValue('Daily');
  await expect(page.locator('input[name="Term"]')).toHaveValue('Summer');

  await page.getByRole('button', { name: 'Check my answers', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Three quick questions' })).toBeVisible();
  await expect(termOptions).toBeHidden();
  await expect(page.locator('#typeDaily')).toBeChecked();
  await page.locator('label[for="typeTerm"]').click();
  await expect(semesterGroup).toBeVisible();
  await expect(page.locator('#termSummer')).toBeChecked();
});
