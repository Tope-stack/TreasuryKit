import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';
import path from 'node:path';

/**
 * Phase 5 acceptance (plan §6): a Preparer and an Approver complete a full month's reconciliation for one account
 * through the UI — every proposal reviewed and every exception matched, resolved or ignored.
 */
const data = (file: string) => path.resolve(import.meta.dirname, '../../tests/TestData/recon', file);
const tenant = `E2E ${new Date().toISOString().replace(/[:.]/g, '-')}`;
const accountName = 'Demo Bank 0123456789 (NGN)';
const month = '2026-02';

test('a Preparer and an Approver reconcile February through the UI', async ({ browser }) => {
  // An Admin sets up the tenant and the bank account.
  const admin = await signIn(browser, 'ada', ['Admin'], { newTenant: tenant });
  await admin.getByLabel('Bank code').fill('058');
  await admin.getByLabel('Bank name').fill('Demo Bank');
  await admin.getByLabel('Account number').fill('0123456789');
  await admin.getByLabel('Mapping profile (CSV/XLSX)').selectOption({ label: 'Generic layout A: CSV, signed amount, dd/MM/yyyy' });
  await admin.getByRole('button', { name: 'Create account' }).click();
  await expect(admin.getByRole('link', { name: accountName })).toBeVisible();
  await admin.context().close();

  // Preparer (who also holds Approver, to exercise maker-checker) uploads expected transactions and the statement.
  const pat = await signIn(browser, 'pat', ['Preparer', 'Approver']);
  await openAccount(pat, 'upload');
  await pat.getByLabel('Expected transactions CSV').setInputFiles(data('expected.csv'));
  await expect(pat.getByTestId('expected-import').first()).toContainText('Completed');
  await pat.getByLabel('Statement file').setInputFiles(data('statement.csv'));
  await expect(pat.getByTestId('statement-import').first()).toContainText('Completed');

  // The import triggers a run automatically.
  await tab(pat, 'Dashboard');
  await expect(pat.getByText(/Last run\s+Completed/)).toBeVisible();
  await expect(pat.getByTestId('kpi-pending')).not.toContainText(/^Pending proposals0$/);

  // Approver: reject one wrong proposal, confirm the rest in bulk.
  const amy = await signIn(browser, 'amy', ['Approver']);
  await openAccount(amy, 'review');
  const proposals = amy.getByTestId('match-card');
  await expect(proposals.first()).toBeVisible();
  const before = await proposals.count();
  const counterSale = proposals.filter({ hasText: 'Counter sale' }).first();
  await counterSale.getByRole('button', { name: 'Reject' }).click();
  await counterSale.getByLabel('Reason (optional)').fill('Not this customer');
  await counterSale.getByRole('button', { name: 'Reject' }).click();
  await expect(proposals).toHaveCount(before - 1);
  await amy.getByLabel('Select all').check();
  await amy.getByRole('button', { name: /Confirm selected/ }).click();
  await expect(amy.getByRole('status')).toContainText('matches confirmed');
  await expect(amy.getByText('No proposals to review for this month.')).toBeVisible();

  // Preparer reruns (the rejected pairing is not proposed again) and works through every exception.
  await tab(pat, 'Dashboard');
  await pat.getByRole('button', { name: 'Run reconciliation' }).click();
  await expect(pat.getByText(/Last run\s+Completed/)).toBeVisible();
  await tab(pat, 'Exceptions');
  await workThroughExceptions(pat);

  // Maker-checker: pat proposed the manual matches and is refused when confirming one.
  await tab(pat, 'Review');
  const own = pat.getByTestId('match-card').first();
  await expect(own).toContainText('another Approver must confirm it');
  await own.getByRole('button', { name: 'Confirm' }).click();
  await expect(pat.getByRole('alert')).toContainText('Match.MakerChecker');

  // A different Approver confirms them (reopening Review loads the new proposals).
  await tab(amy, 'Dashboard');
  await tab(amy, 'Review');
  await amy.getByLabel('Select all').check();
  await amy.getByRole('button', { name: /Confirm selected/ }).click();
  await expect(amy.getByText('No proposals to review for this month.')).toBeVisible();

  // Done: nothing left to review or handle for February.
  await tab(amy, 'Dashboard');
  await expect(amy.getByTestId('kpi-pending')).toContainText(/Pending proposals\s*0/);
  await expect(amy.getByTestId('kpi-exceptions')).toContainText(/Open exceptions\s*0/);
  await expect(amy.getByText('Reconciliation complete: no pending proposals or open exceptions.')).toBeVisible();
});

/** Matches an exception to a candidate with the same amount when there is one; otherwise ignores it with a note. */
async function workThroughExceptions(page: Page) {
  for (let guard = 0; guard < 200; guard++) {
    const cards = page.getByTestId('exception-card');
    await expect(cards.or(page.getByText('No open exceptions.')).first()).toBeVisible();
    if ((await cards.count()) === 0) return;

    // Pin the card by its exception id: `cards.first()` would move on to the next card once this one goes.
    const label = (await cards.first().getAttribute('aria-label'))!;
    const card = page.locator(`[data-testid="exception-card"][aria-label="${label}"]`);
    const amount = (await card.locator('table.items td.num').first().innerText()).trim();
    const sameAmount = card.locator('table.candidates tbody tr').filter({ has: page.locator('td.num', { hasText: exact(amount) }) });

    if ((await sameAmount.count()) > 0) {
      // Judge by the API's answer to this click, not by banners left over from the previous one.
      const [response] = await Promise.all([
        page.waitForResponse((r) => r.url().endsWith('/api/recon/matches') && r.request().method() === 'POST'),
        sameAmount.first().getByRole('button', { name: 'Match with this' }).click(),
      ]);
      if (response.status() === 201) {
        await expect(card).toBeHidden(); // a manual match resolves the exception
      } else {
        expect(response.status()).toBe(409); // e.g. the candidate went to the other half of an ambiguous pair
        await expect(page.getByRole('alert')).toContainText('Match.ItemsAlreadyMatched');
        await ignore(card, 'Candidate already matched elsewhere');
      }
    } else {
      await ignore(card, 'No counterpart this month');
    }
  }
  throw new Error('Exceptions did not clear');
}

async function ignore(card: Locator, note: string) {
  await card.getByRole('button', { name: 'Ignore' }).click();
  await card.getByLabel('Note').fill(note);
  await card.getByRole('button', { name: 'Ignore' }).click();
  await expect(card).toBeHidden();
}

async function signIn(browser: Browser, user: string, roles: string[], { newTenant }: { newTenant?: string } = {}): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await page.goto('/login');
  if (newTenant) {
    await page.getByLabel('New tenant name').fill(newTenant);
  } else {
    await expect(page.getByRole('option', { name: tenant })).toBeAttached(); // the tenant list loads asynchronously
    await page.getByLabel('Tenant', { exact: true }).selectOption({ label: tenant });
  }
  await page.getByLabel('User name').fill(user);
  for (const role of ['Admin', 'Preparer', 'Approver', 'Viewer']) {
    await page.getByLabel(role, { exact: true }).setChecked(roles.includes(role));
  }
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Bank accounts' })).toBeVisible();
  return page;
}

async function openAccount(page: Page, section: string) {
  await page.getByRole('link', { name: accountName }).click();
  await page.getByLabel('Month').fill(month);
  await tab(page, section[0]!.toUpperCase() + section.slice(1));
}

async function tab(page: Page, name: string) {
  await page.getByRole('navigation', { name: 'Account sections' }).getByRole('link', { name, exact: true }).click();
}

const exact = (text: string) => new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
