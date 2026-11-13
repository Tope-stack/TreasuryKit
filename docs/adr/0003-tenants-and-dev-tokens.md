# 0003 — Tenant model and development token issuer

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

Every table is tenant-scoped, and tenant identity comes from a JWT claim. The plan, however, had no way to create a tenant, and Recon must seed default matching rules "on tenant creation". There is no identity provider yet.

## Decision

- **Tenants:** Core owns `core.tenants` (`Tenant` aggregate: name, IANA business time zone defaulting to `Africa/Lagos`). `CreateTenantCommand` publishes the `TenantCreated` integration event (`Core.Contracts`) through the outbox. Recon subscribes to it in Phase 3 to seed its rules.
- **Dev endpoints:** in Development only, the Api maps two anonymous endpoints:
  - `POST /dev/tenants` creates a tenant.
  - `POST /dev/token` mints an HS256 JWT with the claims `sub`, `tenant_id` and `role[]`, where each role is one of `Admin`, `Preparer`, `Approver`, `Viewer`.
- **Validation:** JWT settings (issuer, audience, signing key of at least 32 characters) are checked at startup. Outside Development the signing key must come from the environment or a secret store.
- **Policies:** `CanView`, `CanPrepare`, `CanApprove` and `AdminOnly`, each requiring a `tenant_id` claim. Maker-checker (proposer ≠ approver) is enforced in the command handlers, not in the policies.
- **Request context:** `RequestContextMiddleware` copies the claims into the scoped `TenantContext`/`UserContext`. The event bus sets them per message from `IIntegrationEvent.TenantId`, with user `system`.

## Consequences

- Tenant isolation fails closed: with no tenant, tenant-filtered queries return no rows.
- A real identity provider (OIDC) replaces `/dev/token` later. Endpoints and policies stay unchanged as long as the provider supplies the same claims.
