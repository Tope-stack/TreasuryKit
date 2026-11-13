import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against the real Api (serving the built UI), Worker and PostgreSQL.
 * Locally: `docker compose up -d`, `npm run build`, then `npm run e2e` (starts the Api and Worker unless already running).
 */
const baseURL = process.env.TREASURYKIT_URL ?? 'http://localhost:5080';

// Pass the caller's environment through (e.g. ConnectionStrings__TreasuryKit in CI).
const inherited = Object.fromEntries(Object.entries(process.env).filter((e): e is [string, string] => e[1] !== undefined));

export default defineConfig({
  testDir: './e2e',
  timeout: 300_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL, trace: 'retain-on-failure', ...devices['Desktop Chrome'] },
  webServer: [
    {
      command: 'dotnet run --project ../src/Host/TreasuryKit.Api --no-launch-profile',
      url: `${baseURL}/health`,
      reuseExistingServer: true,
      timeout: 180_000,
      env: { ...inherited, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: baseURL },
    },
    {
      // The Worker has no HTTP endpoint; Playwright waits on the Api's health URL instead.
      command: 'dotnet run --project ../src/Host/TreasuryKit.Worker --no-launch-profile',
      url: `${baseURL}/health`,
      reuseExistingServer: true,
      timeout: 180_000,
      env: { ...inherited, DOTNET_ENVIRONMENT: 'Development' },
    },
  ],
});
