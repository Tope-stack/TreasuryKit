# TreasuryKit — working rules

See `IMPLEMENTATION_PLAN.md` for architecture and phases; `docs/adr/` for decisions that deviate from it.

## Rules

- Stack: **.NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL 16+, xUnit, Testcontainers, NetArchTest**.
- Architecture: **modular monolith**. Modules never reference another module's `Domain`, `Application` or `Infrastructure` projects — only its `*.Contracts` project.
- Each module owns its own **PostgreSQL schema**. No cross-schema joins or foreign keys.
- Cross-module communication: **Contracts query interfaces** (sync) or **integration events via outbox** (async).
- Money: always the `Money` value object (`decimal` amount + `Currency`). Never `double`/`float`. DB column type `numeric(19,4)`.
- All writes are **idempotent** where externally triggered (imports, event handlers, API commands with `Idempotency-Key`).
- Every table carries `tenant_id`; tenant filtering via EF global query filter. Never bypass it outside of tests/migrations. (Exceptions: ADR 0004.)
- Use `Result<T>` for expected failures; exceptions only for truly exceptional cases.
- Async all the way; pass `CancellationToken` everywhere.
- Tests are required for every rule, parser and handler. Architecture tests must pass.
- Present a plan before writing code for any phase or non-trivial change.
- Keep output concise. No speculative features outside the current phase.

## Workflow

- One branch per phase (`phase-N-<slug>`), merged to `main` via PR once acceptance criteria pass and CI is green.
- Record deviations from the plan in `docs/adr/NNNN-title.md`.

## Conventions

- Assemblies/namespaces are `TreasuryKit.<Project>` (set in `Directory.Build.props`), e.g. `TreasuryKit.Core.Domain`.
- Package versions live only in `Directory.Packages.props`. Warnings are errors.
- Module DbContexts derive from `ModuleDbContext`; register with `AddModuleDbContext<T>(schema)`.
- Publish integration events with `IModuleUnitOfWork.Publish(...)` before `SaveChangesAsync`; subscribe with `AddIntegrationEventHandler<TEvent, THandler, TSubscriberDbContext>()`.
- Queue background work with `IModuleUnitOfWork.Enqueue(job)`; register with `AddJobHandler<TJob, THandler>()`. Handlers must be idempotent (retries, lease expiry).
- Uploaded files go through `IFileStore`: local disk under `FileStore:RootPath` (shared by Api and Worker), or PostgreSQL with `FileStore:Provider=Database` (ADR 0011).
- `Hosting:RunBackgroundJobs=true` runs the Worker's background processing inside the Api (single-service hosts, ADR 0011).
- Cross-module reads go through `*.Contracts` query interfaces (e.g. `ICoreStatementQueries`), implemented in the owning module's Infrastructure.
- Test helpers that drive the outbox/job loop must drain until empty: each dispatcher call handles one batch, and other tests leave work behind.
- Endpoints the UI uses declare `.Produces<T>()` with named response records, so the generated client stays typed.
- New module: add it to the module list in both hosts (`TreasuryKit.Api`, `TreasuryKit.Worker`) and to `ArchitectureRules.Modules`.

## Commands

- Start Postgres (port 5433): `docker compose up -d`
- Api (applies migrations in Development): `dotnet run --project src/Host/TreasuryKit.Api` → http://localhost:5080
- Worker: `dotnet run --project src/Host/TreasuryKit.Worker`
- Tests (needs Docker): `dotnet test`
- Regenerate synthetic statements (golden files + manifest): `dotnet run --project tools/StatementGen -- --out tests/TestData/statements`
- Regenerate the reconciliation dataset (statement, expected CSV, answer key): `dotnet run --project tools/StatementGen -- --recon --out tests/TestData/recon`
- Web UI: `cd web && npm run dev` (proxy to :5080); `npm run lint && npm run typecheck && npm test`; `npm run build` (into the Api's wwwroot); `npm run e2e` (Playwright, needs Postgres)
- Regenerate the UI's API types after changing endpoints (Api running): `cd web && npm run gen:api`
- Build images (repo root): `docker build -f src/Host/TreasuryKit.Api/Dockerfile -t treasurykit-api .` (and `TreasuryKit.Worker`)
- Demo deployment: `deploy/` (compose, Caddyfile, backups); guide in `docs/deploy/vm.md`. The dev sign-in is enabled only in Development or with `Auth:EnableDevLogin` (never in Production).
- Render free tier: `render.yaml` Blueprint; guide in `docs/deploy/render.md`.
- Demo seed: `Demo:SeedOnStartup` (on in Demo) seeds "Demo Trading Ltd" with `tests/TestData/recon` on an empty database (ADR 0012). Regenerating that dataset changes the demo too.
- Add migration: `dotnet ef migrations add <Name> --project src/Modules/<Module>/<X>.Infrastructure --context <X>DbContext --output-dir Persistence/Migrations`
