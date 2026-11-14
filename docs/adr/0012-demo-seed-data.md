# 0012 — Demo deployments seed themselves with the synthetic February data

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

A fresh demo database is empty, so the sign-in page offers no tenant and every screen is blank until someone uploads files by hand. On Render's free tier the database is recreated every month (ADR 0011), which would mean repeating that each time. Copying a local database was also rejected: local databases are full of test tenants, and the copy would have to be redone after every refresh.

## Decision

- **The seeder:** `DemoSeeder` runs at Api start-up when `Demo:SeedOnStartup=true` (on in `appsettings.Demo.json`), after migrations.
- **When it acts:** **only when the database has no tenants.** It never touches a database that already has data, and a restart doesn't seed again.
- **What it creates:**
  - the tenant **Demo Trading Ltd**;
  - the account Demo Bank `0123456789` (NGN, Generic layout A);
  - an upload of `tests/TestData/recon/expected.csv`, then `statement.csv`, through the normal commands. The background jobs import them, and the import triggers reconciliation exactly as in the tests: 64 auto-confirmed, 14 proposals and the exceptions, ready to demonstrate.
- **Where the files come from:** the two CSVs are embedded in the Api assembly from `tests/TestData/recon`, so the demo always shows the dataset the tests verify. The Docker build context includes just those two files.
- **If it fails:** the error is logged and the Api still starts.
- **Sign-in page in Demo:**
  - it explains the sample data and preselects **Demo Trading Ltd**;
  - it shows an error with a retry button when the tenant list can't load, instead of an empty dropdown;
  - the UI's own requests use absolute URLs, so they also work on pages opened with credentials in the URL.

## Consequences

- **Fresh databases** start as the same known demo. After Render's monthly database refresh, the demo restores itself.
- **Existing databases are skipped:** to get the seeded demo on one that already has tenants, recreate the database.
- **End-to-end tests** run the Api with `Hosting:RunBackgroundJobs=true` instead of a separate Worker. Playwright could not tell whether a Worker without its own URL was running, so it never started one.
