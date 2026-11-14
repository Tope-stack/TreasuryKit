# 0010 — Demo deployment on a single VM, with the dev sign-in behind a password gate

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

We want a cheap, public demo of TreasuryKit. A real identity provider is not in place yet, and the only sign-in is the development one (ADR 0003): choose any tenant, user and roles. Uploaded files are on local disk and must be shared by the Api and the Worker.

## Decisions

1. **One VM with Docker Compose** (e.g. Hetzner CX22). Caddy, Api, Worker, PostgreSQL and a backup job run on it. The Api and Worker share a `files` volume, so `IFileStore` needs no changes. PostgreSQL is never published. Managed platforms would need an S3-compatible file store first.
2. **A `Demo` environment** (`appsettings.Demo.json`):
   - turns on `Auth:EnableDevLogin`;
   - applies migrations on start-up;
   - stores files under `/data/files`.
3. **Guard rails around the dev sign-in:**
   - **Off by default:** `/dev/*` endpoints are mapped only in Development, or when `Auth:EnableDevLogin=true`.
   - **Never in Production:** the Api refuses to start in `Production` with the flag on.
   - **Logged:** when enabled outside Development, it logs a warning at start-up.
   - **Visible:** the UI shows a "Demo environment — synthetic data only" banner, from `GET /dev/environment`.
4. **A password gate in Caddy.**
   - **What's gated:** HTTP basic auth applies to every request *without* a bearer token. The app's API calls send `Authorization: Bearer …`, which would clash with basic auth's use of the same header; the Api validates those tokens itself.
   - **Why that's safe:** bearer tokens can only be obtained from `/dev/token`, which is behind the gate. Without the shared password, nothing usable is reachable.
5. **HTTPS:** Caddy obtains and renews Let's Encrypt certificates automatically for `SITE_ADDRESS` (a domain, or an `sslip.io` name derived from the VM's IP). It also adds HSTS and basic security headers. The Api trusts forwarded headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`).
6. **Images:**
   - **Builds:** GitHub Actions builds the Api image (with the web UI built into `wwwroot`) and the Worker image on every push to `main`, and pushes them to GHCR as `:latest` and `:<sha>`.
   - **Deploys:** a manual workflow run deploys over SSH, pinning `TAG` to the commit. Rollback is setting `TAG` back.
   - **Runtime:** both images run as the non-root `app` user on the ASP.NET runtime image.
7. **Backups:** a `pg_dump` (custom format) at start-up and every 24 hours, kept for 7 days on the VM. Restoring was verified by loading a dump into a fresh database. Off-site copies are a manual step for now.

## Consequences

- **Synthetic data only.** Anyone with the shared password can act as any user. Real customer data needs real sign-in (OIDC) first; at that point the flag stays off and the gate can go.
- **A single point of failure.** That's acceptable for a demo.
- **Moving to a managed platform** later means adding an S3-compatible `IFileStore` and managed PostgreSQL; the images stay as they are.
