# 0004 — Exceptions to "every table carries tenant_id"

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

The working rules say every table carries `tenant_id`, filtered by an EF global query filter. Some tables in the plan don't fit that rule cleanly.

## Decision

1. **Tenant filter mechanics:** the filter applies automatically to every entity implementing `ITenantOwned`, via `ModuleDbContext`. It compares against the context's current tenant, and a null tenant matches nothing.
2. **`FxRate` is global reference data** with no `tenant_id`. Market rates are not tenant secrets. If tenants later need their own rates (e.g. negotiated bank rates), they get a separate tenant-owned table.
3. **`BankMappingProfile.TenantId` is nullable** (null = system-wide profile). Phase 1 gives it a custom filter `TenantId == current || TenantId == null` instead of the generic `ITenantOwned` filter. System profiles are read-only to tenants.
4. **`MatchItem` gets `tenant_id`** (it was missing in §5.1), so the rule holds and RLS can be added later.
5. **Infrastructure tables:** `outbox_messages` and `audit_log` carry a nullable `tenant_id` for traceability but are not filtered, because the dispatchers read across tenants. `inbox_messages` is keyed by message id only. `core.tenants` *is* the tenant.

## Consequences

- Postgres RLS (planned after Phase 4) must exempt the same tables.
