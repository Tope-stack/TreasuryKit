# 0009 — Web UI: React + Vite SPA served by the Api

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

Phase 5 needs a minimal UI so that a Preparer and an Approver can reconcile a month without calling the API by hand. The plan left the framework open; we chose React + Vite.

## Decisions

1. **Stack (`web/`).**
   - React 19 + TypeScript on Vite, with React Router for pages and TanStack Query for loading and caching API data.
   - Plain CSS with variables. It supports light and dark mode, and stacks the layout at narrow widths.
   - No component library: the screens are tables, forms and cards.
2. **Typed client.**
   - **Generation:** `npm run gen:api` runs `openapi-typescript` against the running Api's OpenAPI document. The generated `src/api/schema.d.ts` is committed, and calls go through `openapi-fetch`.
   - **What it relies on:** the endpoints the UI calls declare `.Produces<T>()` and return named response records instead of anonymous objects, so the document has real types.
   - **Quirk:** decimals and integers are typed `number | string`, because the JSON options allow numbers as strings. The UI converts them with `num()`.
3. **One deployable.**
   - **Production:** `npm run build` writes into `src/Host/TreasuryKit.Api/wwwroot` (git-ignored). The Api serves the files and falls back to `index.html` for client routes.
   - **Fallback limits:** the fallback never applies to `/api`, `/dev`, `/health`, `/openapi` or `/scalar`, or to any path with a file extension. Without the last rule, the fallback answered asset requests with HTML.
   - **Development:** the Vite dev server proxies `/api` and `/dev` to the Api, so no CORS setup is needed.
4. **Sign-in is development-only (ADR 0003).**
   - **How it works:** the login page lists tenants (`GET /dev/tenants`, dev only) and requests a token from `/dev/token`. The session (token, tenant, user, roles) is kept in `sessionStorage`.
   - **Role gating:** roles only decide which buttons are shown. The API enforces every rule, including maker-checker.
   - **Later:** an OIDC provider replaces only this page.
5. **Queue endpoints for the UI.**
   - `GET /api/recon/review-queue` and `GET /api/recon/exception-queue` return matches and exceptions with the details of every item involved, assembled through Core's query contracts. This saves the UI one request per item.
   - `GET /api/core/accounts/{id}/imports` and `GET /api/core/expected-transactions/imports` list imports.
6. **Freshness without push.** Work happens in the background and by other users, so the UI polls:
   - **Imports:** every 1.5 seconds while one is pending.
   - **Runs:** every 1.5 seconds while one is active, otherwise every 10 seconds. The summary refreshes whenever the latest run changes.
   - **Queues:** every 10 seconds.
   - **Reviewed matches:** they leave the review queue and the selection immediately, so a stale card can't be bulk-confirmed.

   Server push (SignalR) is a possible later improvement.
7. **"Reconciliation complete"** shows only when a completed run covers the month *and* nothing is pending or open. Zero proposals and zero exceptions are also true before any run.

## Testing

- **Vitest + Testing Library:** component behaviour, with `fetch` stubbed.
- **Playwright (`web/e2e`):** the Phase 5 acceptance flow runs against the real Api, Worker and PostgreSQL. The test:
  - an Admin sets up the account;
  - a Preparer uploads both files;
  - an Approver rejects one proposal and bulk-confirms the rest;
  - the Preparer reruns and works through every exception, and is refused when confirming their own manual match;
  - a different Approver confirms;
  - the dashboard then shows the month complete.
- **CI:** the `web` and `e2e` jobs run these.
