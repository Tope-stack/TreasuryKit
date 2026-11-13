# TreasuryKit — Implementation Plan

> Treasury tooling for African businesses operating across borders. Phase 1 ships **multi-bank statement reconciliation**; the architecture is a modular monolith so FX exposure, cash forecasting, beneficiary validation and import compliance can be added as modules later.
>
> `TreasuryKit` is a placeholder name — rename freely.

---

## 0. How to use this file with Claude Code

1. Put this file at the repo root.
2. Copy **Section 1 (Working rules)** into `CLAUDE.md` so it applies to every session.
3. Build **one phase at a time**. Suggested prompt per phase:
   > "Read IMPLEMENTATION_PLAN.md. Plan Phase N only. Show me the plan before writing code. Stop when the Phase N acceptance criteria pass."
4. Do not start a phase until the previous phase's acceptance criteria pass and tests are green.
5. Record decisions that deviate from this plan in `docs/adr/NNNN-title.md`.

---

## 1. Working rules (copy into CLAUDE.md)

- Stack: **.NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL 16+, xUnit, Testcontainers, NetArchTest**.
- Architecture: **modular monolith**. Modules never reference another module's `Domain`, `Application` or `Infrastructure` projects — only its `*.Contracts` project.
- Each module owns its own **PostgreSQL schema**. No cross-schema joins or foreign keys.
- Cross-module communication: **Contracts query interfaces** (sync) or **integration events via outbox** (async).
- Money: always the `Money` value object (`decimal` amount + `Currency`). Never `double`/`float`. DB column type `numeric(19,4)`.
- All writes are **idempotent** where externally triggered (imports, event handlers, API commands with `Idempotency-Key`).
- Every table carries `tenant_id`; tenant filtering via EF global query filter. Never bypass it outside of tests/migrations.
- Use `Result<T>` for expected failures; exceptions only for truly exceptional cases.
- Async all the way; pass `CancellationToken` everywhere.
- Tests are required for every rule, parser and handler. Architecture tests must pass.
- Present a plan before writing code for any phase or non-trivial change.
- Keep output concise. No speculative features outside the current phase.

---

## 2. Architecture

### 2.1 Modules

| Module | Schema | Owns | Status |
|---|---|---|---|
| **Core** | `core` | Tenants, users/roles, bank accounts, counterparties, bank mapping profiles, statement imports & lines, expected transactions, FX rates | Phase 1 |
| **Reconciliation** | `recon` | Matching rule configs, runs, matches, match items, exceptions | Phase 1 |
| FxExposure | `fx` | Exposure snapshots, scenarios | Future |
| CashForecast | `forecast` | Forecast models, weekly buckets | Future |
| BeneficiaryValidation | `benval` | Validators (IBAN/BIC/CNAPS/NUBAN), validation log | Future |
| ImportCompliance | `compliance` | Form M, PAAR, shipment docs linked to payments | Future |

**Key decision:** statement ingestion and expected transactions live in **Core**, not Reconciliation, because forecasting and FX exposure also consume actual balances and expected flows.

### 2.2 Solution layout

```
TreasuryKit.sln
src/
  Host/
    TreasuryKit.Api/                    # composition root, auth, module discovery, OpenAPI
    TreasuryKit.Worker/                 # background jobs (imports, runs, outbox) — may start inside Api
  BuildingBlocks/
    BuildingBlocks.Domain/              # Entity, AggregateRoot, ValueObject, Money, Currency, Result, IDomainEvent
    BuildingBlocks.Application/         # ICommand/IQuery + handlers, ICurrentTenant, ICurrentUser, IClock, IIntegrationEvent
    BuildingBlocks.Infrastructure/      # ModuleDbContext base, outbox/inbox, dispatcher, tenancy filter, audit interceptor
  Modules/
    Core/
      Core.Domain/
      Core.Application/
      Core.Infrastructure/
      Core.Contracts/                   # public DTOs, query interfaces, integration events
    Reconciliation/
      Recon.Domain/
      Recon.Application/
      Recon.Infrastructure/
      Recon.Contracts/
tests/
  ArchitectureTests/
  Core.Tests/
  Recon.Tests/
  IntegrationTests/                     # Testcontainers Postgres, end-to-end flows
  TestData/                             # anonymized/generated statements (golden files)
docs/
  adr/
docker-compose.yml                      # postgres, (later) seq/otel
```

### 2.3 Module contract

```csharp
public interface IModule
{
    string Name { get; }
    void AddServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder app);   // routes under /api/{module}
}
```

Host discovers `IModule` implementations and registers them. Adding a module = new folder + one registration line.

### 2.4 Cross-module communication

- **Sync reads:** interfaces in `*.Contracts`, e.g. `ICoreStatementQueries`, implemented in the owning module's Infrastructure.
- **Async:** integration events written to the module's `outbox_messages` table in the same transaction as the state change. A background dispatcher delivers them in-process to subscribers. Each subscriber records processed message IDs in its own `inbox_messages` table (idempotent consumption).
- Dispatcher is behind an interface so it can later be swapped for RabbitMQ/MassTransit without changing module code.

### 2.5 Architecture tests (must exist from Phase 0)

- `X.Domain` depends on nothing but `BuildingBlocks.Domain`.
- `X.Application` does not depend on `X.Infrastructure`.
- No module references another module's non-Contracts projects.
- `Contracts` projects do not reference `Domain` projects.

---

## 3. Cross-cutting concerns

| Concern | Approach |
|---|---|
| Multi-tenancy | `tenant_id` on every table; `ICurrentTenant` from JWT claim; EF global query filter. Postgres RLS is a later hardening step. |
| Auth | JWT bearer. Roles: `Admin`, `Preparer`, `Approver`, `Viewer`. Policy-based authorization per endpoint. |
| Maker-checker | Manual matches/unmatches are proposed by a Preparer and confirmed by a **different** Approver. |
| Audit | Append-only `audit_log` per schema, written by an EF `SaveChangesInterceptor` (who, when, entity, before/after JSON). |
| Idempotency | File hash + line fingerprint for imports; `Idempotency-Key` header for POST commands; inbox for events. |
| Concurrency | Optimistic concurrency (`xmin` row version) on aggregates; one active recon run per account via partial unique index. |
| Time | `IClock` abstraction; store UTC `timestamptz`; business dates as `date`. Default business timezone `Africa/Lagos` per tenant. |
| Long-running work | Imports and runs are queued; API returns `202 Accepted` + resource ID to poll. |
| Observability | Serilog structured logs, OpenTelemetry traces/metrics, correlation ID middleware. |
| Errors | RFC 7807 ProblemDetails. |
| Config | Options pattern, validated at startup. |

---

## 4. Core module

### 4.1 Domain model

**BankAccount** (aggregate)
- `Id, TenantId, BankCode, BankName, AccountNumber, Currency, DisplayName, MappingProfileId?, IsActive`
- Unique: `(tenant_id, bank_code, account_number, currency)`

**BankMappingProfile** — data-driven CSV/Excel parsing config
- `Id, TenantId?` (null = system-wide), `BankCode, Name, FileType (Csv|Xlsx)`
- `HeaderRowIndex, SkipRows, Delimiter, DateFormat, Culture`
- `ColumnMap` (jsonb): `{ bookingDate, valueDate, narration, reference, amount | debit+credit, balance }`
- `AmountMode`: `Signed | DebitCreditColumns`
- `OpeningBalanceLocator`/`ClosingBalanceLocator` (optional)

**StatementImport** (aggregate)
- `Id, TenantId, AccountId, FileName, FileHash (SHA-256), Format (Mt940|Camt053|Csv|Xlsx), PeriodStart, PeriodEnd, OpeningBalance, ClosingBalance, LineCount, DuplicateLineCount, Status (Queued|Parsing|Validated|BalanceMismatch|Failed|Completed), Error?, ImportedBy, ImportedAt`
- Unique: `(tenant_id, account_id, file_hash)`

**StatementLine**
- `Id, TenantId, AccountId, ImportId, BookingDate, ValueDate, Amount (signed Money), Narration, NormalizedNarration, BankReference?, ExtractedRefs (jsonb: { nipSessionId?, invoiceRefs[], paymentRefs[] }), CounterpartyNameGuess?, Fingerprint, RunningBalance?`
- Unique: `(tenant_id, account_id, fingerprint)`
- Indexes: `(account_id, value_date)`, `(account_id, amount)`, GIN on `extracted_refs`

**ExpectedTransaction** (aggregate) — payables/receivables; reused by CashForecast later
- `Id, TenantId, AccountId?, Direction (Inflow|Outflow), Amount (Money), OutstandingAmount, ExpectedDate, CounterpartyId?, CounterpartyName, Reference, Description, Source (Manual|Csv|Api), ExternalId?, Status (Open|PartiallySettled|Settled|Cancelled)`
- Unique: `(tenant_id, source, external_id)` where external_id not null

**Counterparty**
- `Id, TenantId, Name, NormalizedName, Aliases[], Country?, DefaultCurrency?`

**FxRate** (used by later modules; table created now)
- `Id, BaseCurrency, QuoteCurrency, Rate, AsOfDate, Source`

### 4.2 Statement parsing

```csharp
public interface IStatementParser
{
    StatementFormat Format { get; }
    bool CanParse(ReadOnlySpan<byte> header, string fileName);
    Task<Result<ParsedStatement>> ParseAsync(Stream stream, ParseContext ctx, CancellationToken ct);
}

public sealed record ParsedStatement(
    string? AccountIdentifier, Currency Currency,
    DateOnly? PeriodStart, DateOnly? PeriodEnd,
    decimal? OpeningBalance, decimal? ClosingBalance,
    IReadOnlyList<ParsedLine> Lines);
```

Implementations:
- `Mt940Parser` — tags `:20: :25: :28C: :60F: :61: :86: :62F:`; handle multi-line `:86:`.
- `Camt053Parser` — ISO 20022 XML, `Ntry` elements, `CdtDbtInd`, `AcctSvcrRef`, `RmtInf`.
- `ProfileDrivenTabularParser` — CSV (CsvHelper) and XLSX (ClosedXML/ExcelDataReader) driven by `BankMappingProfile`.

`ParserResolver` picks a parser by sniffing the header; CSV/XLSX uses the account's mapping profile.

### 4.3 Narration normalization & reference extraction

`INarrationNormalizer`:
- Uppercase, collapse whitespace, strip punctuation noise.
- Strip common Nigerian bank prefixes: `NIP/`, `NIP TRF`, `TRF/`, `FRM`, `TO`, `MOB/`, `USSD/`, `POS/`, `WEB/` (configurable list).

`IReferenceExtractor` (pluggable rules, ordered):
- **NIP session ID**: 30-digit numeric sequence.
- Invoice refs: configurable regexes per tenant (e.g. `INV[-/]?\d+`).
- Payment refs / SWIFT refs (e.g. 16-char alphanumeric UETR fragments).
- Counterparty name guess: text after `FRM`/`FROM`/`TO` tokens.

### 4.4 Idempotency & integrity

1. **File level**: compute SHA-256 before parsing. Existing `(account, file_hash)` → return existing import (HTTP 200, no reprocess).
2. **Line level**: `Fingerprint = SHA256(accountId | valueDate | amount | bankReference ?? normalizedNarration | occurrenceIndex)` where `occurrenceIndex` is the ordinal of identical tuples within the same file. Insert with `ON CONFLICT DO NOTHING`; count skipped as `DuplicateLineCount` (handles overlapping statement periods).
3. **Balance validation**: if opening & closing balances present, `opening + Σ(lines) == closing` else status `BalanceMismatch` (lines kept but flagged; recon run blocked until acknowledged by Admin).
4. Account/currency on file must match target account; mismatch → `Failed`.

### 4.5 Integration events (Core.Contracts)

- `StatementImported { TenantId, AccountId, ImportId, PeriodStart, PeriodEnd, NewLineCount }`
- `ExpectedTransactionCreated/Updated/Cancelled`
- Consumes: `MatchConfirmed`, `MatchReversed` → updates `ExpectedTransaction.OutstandingAmount` / `Status`.

### 4.6 Query contracts (Core.Contracts)

- `ICoreStatementQueries.GetUnmatchedCandidatesAsync(accountId, period, excludeIds)`
- `ICoreExpectedTransactionQueries.GetOpenAsync(accountId?, currency, dateFrom, dateTo)`
- `ICoreBalanceQueries.GetLatestBalancesAsync(tenantId)` (for future modules)

> Recon tracks matched state itself (in `recon` schema); Core queries accept exclusion sets or Recon keeps a local projection of line/expected IDs. **Decision:** Recon maintains `recon.reconciled_items(item_type, item_id, match_id)` and filters out matched IDs client-side; revisit if volumes require a projection.

### 4.7 Endpoints (`/api/core`)

```
POST   /accounts
GET    /accounts
GET    /accounts/{id}
POST   /mapping-profiles
GET    /mapping-profiles
POST   /accounts/{id}/statements            multipart upload → 202 { importId }
GET    /imports/{id}
GET    /accounts/{id}/lines?from&to&status
POST   /imports/{id}/acknowledge-mismatch   Admin only
POST   /expected-transactions               single (Idempotency-Key)
POST   /expected-transactions/import        CSV → 202
GET    /expected-transactions?status&from&to&currency
PATCH  /expected-transactions/{id}
POST   /expected-transactions/{id}/cancel
POST   /counterparties   GET /counterparties
```

---

## 5. Reconciliation module

### 5.1 Domain model

**MatchingRuleConfig**
- `Id, TenantId, RuleKey (ExactReference|AmountDateWindow|SplitPayment|Tolerance), Order, Enabled, Parameters (jsonb)`
- Seeded defaults per tenant on creation.

**ReconciliationRun** (aggregate)
- `Id, TenantId, AccountId, PeriodStart, PeriodEnd, Status (Queued|Running|Completed|Failed), TriggeredBy (Event|User), StartedAt, CompletedAt, Stats (jsonb: linesConsidered, autoConfirmed, proposed, exceptions, durationMs)`
- Partial unique index: one `Running` run per `(tenant_id, account_id)`.

**Match** (aggregate)
- `Id, TenantId, RunId?, Type (OneToOne|OneToMany|ManyToOne), RuleKey | Manual, Confidence (0–1), ScoreBreakdown (jsonb), Status (Proposed|Confirmed|Rejected|Reversed), ProposedBy, ProposedAt, ConfirmedBy?, ConfirmedAt?, Reason?`
- Invariants: Σ statement-side allocations == Σ expected-side allocations (same currency, or within configured tolerance with difference recorded as `Adjustment`); ConfirmedBy ≠ ProposedBy for manual matches.

**MatchItem**
- `Id, MatchId, ItemType (StatementLine|ExpectedTransaction), ItemId, AllocatedAmount`

**ReconciledItem** (projection for fast exclusion)
- `(tenant_id, item_type, item_id)` unique → `match_id`

**ReconException**
- `Id, TenantId, RunId, ItemType, ItemId, Reason (NoCandidate|MultipleCandidates|BelowThreshold|CurrencyMismatch), Candidates (jsonb: top 5 with scores), Status (Open|Resolved|Ignored), ResolvedBy?, ResolutionNote?`

### 5.2 Matching engine

```csharp
public interface IMatchingRule
{
    string Key { get; }
    IAsyncEnumerable<MatchCandidate> FindAsync(MatchingPool pool, RuleParameters p, CancellationToken ct);
}

public sealed class MatchingPool  // unmatched statement lines + open expected txns for the account/period
{
    // supports Remove(...) as matches are accepted so later rules see the shrinking pool
}

public sealed record MatchCandidate(
    MatchType Type,
    IReadOnlyList<ItemRef> StatementSide,
    IReadOnlyList<ItemRef> ExpectedSide,
    decimal Confidence,
    IReadOnlyDictionary<string, decimal> ScoreBreakdown);
```

**Pipeline:** load pool → for each enabled rule in order → candidates → resolve conflicts (an item can only be in one accepted candidate; highest confidence wins; ties → exception `MultipleCandidates`) → apply thresholds → remove matched items from pool → next rule → leftover items become exceptions.

**Thresholds (configurable):** `autoConfirm ≥ 0.95`, `propose ≥ 0.70`, else exception.

### 5.3 Rules

| Order | Rule | Logic | Default params | Outcome |
|---|---|---|---|---|
| 1 | `ExactReference` | NIP session ID / invoice ref / payment ref on line equals `ExpectedTransaction.Reference`, **and** amount equal, **and** direction matches | — | Confidence 1.0 → auto-confirm |
| 2 | `AmountDateWindow` | Equal amount, same direction, `|valueDate − expectedDate| ≤ window`, exactly one candidate. Score = base 0.80 + date proximity (≤0.10) + counterparty name similarity via Jaro-Winkler on normalized names/aliases (≤0.10) | `windowDays: 3`, `nameSimilarityMin: 0.85` | Auto if ≥ 0.95 else proposed |
| 3 | `SplitPayment` | One expected ↔ many lines (or vice versa) whose sum equals target within window. Bounded subset-sum: `maxItems: 5`, candidates pre-filtered by window + direction, search capped by `maxCombinations` | `windowDays: 5`, `maxItems: 5`, `maxCombinations: 20000` | Always proposed |
| 4 | `Tolerance` | Amount differs within `max(absTolerance, pct × amount)` — bank charges, FX rounding. Difference recorded as adjustment | `absTolerance: 0` (per currency), `pct: 0.005` | Always proposed, never auto |

### 5.4 Run behaviour

- **Triggers:** `StatementImported` event (auto) or `POST /runs` (manual).
- **Idempotent:** confirmed matches are never touched; reruns only process the unmatched pool. Previously proposed-but-unreviewed matches from older runs are superseded (status `Rejected`, reason `Superseded`) before re-proposing.
- **Blocked** if any import in the period is `BalanceMismatch` and not acknowledged.
- **Explainable:** every match stores `RuleKey` + `ScoreBreakdown`.

### 5.5 Manual operations (maker-checker)

- `ProposeManualMatch` (Preparer) → `Proposed`, `RuleKey = Manual`.
- `ConfirmMatch` (Approver, must differ from proposer for manual matches; auto-matches confirmable by any Approver).
- `RejectMatch` (Approver) → items return to pool.
- `ReverseMatch` (Approver, with reason) → confirmed match reversed, `MatchReversed` event, items return to pool.
- `ResolveException` / `IgnoreException` (with note).

### 5.6 Integration events (Recon.Contracts)

- `MatchConfirmed { TenantId, MatchId, Items[{type, id, allocatedAmount}] }`
- `MatchReversed { TenantId, MatchId, Items[...] }`
- `ReconciliationRunCompleted { TenantId, AccountId, RunId, Stats }`

### 5.7 Endpoints (`/api/recon`)

```
POST   /runs                       { accountId, from, to } → 202 { runId }
GET    /runs/{id}
GET    /runs?accountId
GET    /matches?status&accountId&from&to
GET    /matches/{id}
POST   /matches                    manual propose (Idempotency-Key)
POST   /matches/{id}/confirm
POST   /matches/{id}/reject
POST   /matches/{id}/reverse
POST   /matches/confirm-batch      bulk confirm proposed matches
GET    /exceptions?status&accountId
POST   /exceptions/{id}/resolve
POST   /exceptions/{id}/ignore
GET    /rules       PUT /rules/{key}
GET    /reports/summary?accountId&from&to       matched %, unmatched totals, aging
GET    /reports/export?accountId&from&to&format=csv|xlsx
```

---

## 6. Delivery phases

Each phase ends with green build, passing tests and the acceptance criteria met.

### Phase 0 — Foundation
- Solution + projects per §2.2; central package management (`Directory.Packages.props`); nullable + warnings-as-errors.
- BuildingBlocks: `Entity`, `AggregateRoot`, `ValueObject`, `Money`, `Currency` (ISO 4217 list incl. NGN, USD, GBP, EUR, CNY), `Result<T>`, CQRS abstractions, `IClock`.
- `ModuleDbContext` base with schema, tenancy filter, audit interceptor, outbox table.
- Outbox dispatcher + inbox (in-process).
- Module discovery in Host; health checks; Serilog; ProblemDetails; OpenAPI.
- JWT auth with a dev token issuer endpoint (dev only) and the four roles.
- `docker-compose.yml` (Postgres). GitHub Actions: build + test.
- Architecture tests per §2.5.

**Acceptance:** `docker compose up` + `dotnet run` serves `/health`; architecture tests pass; an outbox round-trip test (publish → handler invoked exactly once on redelivery) passes.

### Phase 1 — Core: accounts & tabular import
- BankAccount, Counterparty, BankMappingProfile CRUD.
- `ProfileDrivenTabularParser` (CSV + XLSX).
- Import pipeline: upload → queue → parse → normalize → fingerprint → insert → balance validation → `StatementImported`.
- Seed 2–3 sample mapping profiles from generated test statements (see §7).

**Acceptance:** uploading the same file twice creates one import; uploading two overlapping statements produces no duplicate lines; a deliberately unbalanced file ends `BalanceMismatch`; event appears in outbox.

### Phase 2 — Core: MT940, CAMT.053, normalization
- `Mt940Parser`, `Camt053Parser`, `ParserResolver`.
- `INarrationNormalizer`, `IReferenceExtractor` (NIP session ID, invoice regex, counterparty guess).

**Acceptance:** golden-file tests for each format pass; NIP session IDs extracted from sample narrations with 100% accuracy on test set.

### Phase 3 — Expected transactions + core matching
- ExpectedTransaction CRUD + CSV import (idempotent by `external_id`).
- Recon schema, `MatchingRuleConfig` seeding, `ReconciliationRun`, engine pipeline.
- Rules `ExactReference`, `AmountDateWindow`.
- Exceptions with top-5 candidates.
- `MatchConfirmed` → Core updates expected transaction status.

**Acceptance:** on the generated dataset (§7), ≥ 90% of "clean" items auto-match; rerunning a run produces no duplicate matches; concurrent run requests for the same account → one runs, other rejected with 409.

### Phase 4 — Advanced matching & controls
- Rules `SplitPayment`, `Tolerance`.
- Manual propose/confirm/reject/reverse with maker-checker.
- Bulk confirm; summary report; CSV/XLSX export.
- `Idempotency-Key` middleware for POST commands.

**Acceptance:** split payments (2–5 parts) proposed correctly; same user cannot propose and confirm a manual match (403); reversal returns items to pool and restores expected transaction outstanding amount.

### Phase 5 — Minimal UI
- Upload page, import status, match review queue (approve/reject with score breakdown), exceptions queue, summary dashboard.
- Framework: **TBD (React + Vite or Blazor)** — decide before starting.

**Acceptance:** a Preparer and an Approver can complete a full month's reconciliation for one account through the UI.

---

## 7. Test data strategy

- No real customer data in the repo. Build a **statement generator** (`tools/StatementGen`) that produces matching pairs of bank statements (CSV/XLSX/MT940/CAMT.053) and expected-transaction CSVs with controlled scenarios:
  - clean 1:1 with NIP session ID in narration
  - 1:1 without reference (amount + date only)
  - date drift (1–5 days)
  - split payments (2–5 parts)
  - bank charges deducted (tolerance)
  - duplicates/overlapping periods
  - true unmatched items on both sides
  - multiple equal-amount candidates (ambiguity)
- Generator emits an **answer key** so tests can assert match precision/recall.
- Mapping profiles: model on the column layouts of common Nigerian bank exports; refine once real (anonymized) samples are available.

**Quality bar:** auto-confirmed matches must have **100% precision** on the generated dataset — a wrong auto-match is worse than an exception.

---

## 8. Future modules — how they plug in

| Module | Reads from Core | Reads from Recon | New in its schema |
|---|---|---|---|
| **FxExposure** | Latest balances per currency, open expected transactions, FX rates | — | Exposure snapshots, scenario definitions (e.g. NGN ±10/20/30%), net open position report |
| **CashForecast** | Balances, expected transactions | Confirmed matches (actuals vs expected → slippage stats per counterparty) | Forecast versions, 13-week buckets per currency, shortfall alerts |
| **BeneficiaryValidation** | Counterparties | — | Validators: IBAN (mod-97), BIC format, CNAPS (12-digit), NUBAN check digit; validation log. Exposed as sync API |
| **ImportCompliance** | Expected transactions (outflows to foreign suppliers), counterparties | Confirmed matches (proof of payment) | Form M, PAAR, proforma invoice, shipment milestones; status workflow |

Rules for every new module: own schema, own Contracts project, consume Core/Recon only through Contracts or events, arch tests updated, one registration line in Host.

---

## 9. Open decisions

| # | Decision | Default |
|---|---|---|
| 1 | UI framework | Decide at Phase 5 |
| 2 | Background jobs | Hosted `BackgroundService` + Postgres-backed queue table; consider Hangfire/Quartz if scheduling grows |
| 3 | File storage for uploads | Local disk in dev behind `IFileStore`; S3-compatible later |
| 4 | Event transport | In-process outbox dispatcher; RabbitMQ only if modules are split out |
| 5 | Postgres RLS | After Phase 4 as hardening |
| 6 | Product name | `TreasuryKit` placeholder |
