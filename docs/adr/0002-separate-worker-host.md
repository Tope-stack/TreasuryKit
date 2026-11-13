# 0002 — Separate Worker host from the start; Api owns migrations

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

The plan allows background work to "start inside Api". We chose to run `TreasuryKit.Worker` as its own process from Phase 0, so that slow imports and reconciliation runs never compete with request handling, and so the deployment shape is settled early.

## Decision

- **Both hosts register the same modules** through `IModule.AddServices`. Each host has one module list line, so adding a module means one line in each.
- **The Api** serves HTTP, accepts commands and writes integration events to the outbox. It does **not** run the outbox dispatcher.
- **The Worker** runs `OutboxBackgroundService`, and from Phase 1 the job processors.
- **Migrations are applied only by the Api**: always in Development, and elsewhere when `Database:MigrateOnStartup=true`. The Worker never migrates, so the two processes cannot race. If the Worker starts before the schema exists, it logs the error and retries on the next poll.
- **Several Worker instances are safe**: outbox batches are claimed with `FOR UPDATE SKIP LOCKED`.

## Consequences

- Local development runs two processes: `dotnet run` for the Api and another for the Worker.
- Integration tests host only the Api and drive `OutboxDispatcher.DispatchPendingAsync` directly, so they stay deterministic.
- In production, migrations may later move to a dedicated release step (e.g. an EF bundle). The `MigrateOnStartup` flag keeps that option open.
