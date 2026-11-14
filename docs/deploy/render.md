# Deploying the demo to Render's free tier

On Render's free tier, TreasuryKit runs as **one web service** plus a **free PostgreSQL**. The Api serves the web UI and also runs the background work (imports, reconciliation runs, outbox), because free plans have no background workers. Uploaded files are stored in PostgreSQL, because free web services have no persistent disk. ADR 0011 explains the design. For an always-on setup, see the single-VM guide in [vm.md](vm.md).

> **Demo only.** The development sign-in is on, behind a shared demo password: anyone with that password can act as any tenant and role. Use synthetic data only (for example the files in `tests/TestData/recon`).

## What to expect from the free tier

Check Render's current terms; this is how it worked when this guide was written.

- **Sleep:** the service sleeps after 15 minutes without traffic. The next visit wakes it, which takes about a minute. Background work pauses while it sleeps, and an upload wakes it.
- **Database expiry:** the free database **expires 30 days after creation**, and is deleted after a further 14-day grace period unless upgraded. Delete and recreate it (section "Monthly database refresh"), then reload the demo data.
- **Limits:** 1 GB of database storage and 750 free instance hours a month per workspace. One service running all month fits.

## 1. Create the services from the Blueprint

1. Sign in to Render and connect your GitHub account, giving it access to the TreasuryKit repository.
2. Go to **New → Blueprint**, choose the repository and branch `main`. Render reads `render.yaml` and shows:
   - a web service **treasurykit-demo** (free, built from `src/Host/TreasuryKit.Api/Dockerfile`);
   - a database **treasurykit-db** (free PostgreSQL).
3. When asked for **DemoGate__Password**, enter the password you'll share with demo viewers. The username is `demo`.
4. Click **Apply**. The first build takes several minutes: it builds the web UI and the Api inside Docker. On start-up the Api applies the database migrations.

Render generates the JWT signing key itself (`generateValue`), and connects the database through its internal connection string.

## 2. Open it

Open the service URL shown in Render (e.g. `https://treasurykit-demo.onrender.com`).
1. The browser asks for the demo password: user `demo`, plus the password you set.
2. The TreasuryKit sign-in follows, with a "Demo environment" banner on every page.

### Loading the demo data

1. Sign in with a new tenant, user `admin` and role **Admin**, and create an account:
   - bank code `058`, account number `0123456789`, currency `NGN`;
   - mapping profile **Generic layout A**.
2. Sign in again as a **Preparer**. Under the account's Upload tab, upload `tests/TestData/recon/expected.csv`, then `tests/TestData/recon/statement.csv`. Reconciliation starts automatically.
3. Set the month to **February 2026**. Use an **Approver** for the Review tab, and the Preparer for Exceptions.

## Updating

`autoDeploy` is on: every push to `main` rebuilds and redeploys the service. You can also use **Manual Deploy** in the dashboard.

## Monthly database refresh

When the free database nears its 30-day expiry (Render emails you):

1. In the dashboard, delete **treasurykit-db**.
2. Open the Blueprint and click **Sync**. Render recreates the database and reconnects the service.
3. The service restarts and the Api applies the migrations to the empty database. Reload the demo data.

Upgrading the database to a paid plan avoids the refresh.

## Changing the demo password

Service **treasurykit-demo → Environment → DemoGate__Password**, then save. Render restarts the service.

## Troubleshooting

- **The first request is slow:** the service was asleep. Expected on the free tier.
- **Asked for the password again and again:** the username is `demo` (`DemoGate__Username`), and the password is the `DemoGate__Password` value.
- **The service fails health checks after the database was recreated:** check the logs. The connection string comes from the database, so a Blueprint **Sync** is needed after recreating it.
- **Imports stay "Queued":** background work runs only while the service is awake, so open the site to wake it. It also requires `Hosting__RunBackgroundJobs=true`.
