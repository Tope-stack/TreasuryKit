# 0006 — How extracted references are stored for matching

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

Phase 3's `ExactReference` rule compares references found on statement lines with `ExpectedTransaction.Reference`. Phase 2 decides what is extracted, in what form, and where it is stored.

## Decision

- **`statement_lines.extracted_refs` (jsonb)** holds `{ nipSessionId, invoiceRefs[], paymentRefs[] }`.
  - **Invoice references** are stored in **canonical form**: upper-case letters and digits only, so `INV-1041`, `inv 1041` and `INV/1041` all become `INV1041`. Phase 3 must canonicalize `ExpectedTransaction.Reference` the same way, via `ExtractedReferences.Canonical`.
  - **NIP session IDs** are stored as the 30 digits. When a narration contains several, the one with a valid `yyMMddHHmmss` timestamp is chosen.
  - **Payment references** include CAMT `EndToEndId`, UETRs and 16-character alphanumeric tokens.
- **Structured counterparty data** from CAMT (debtor/creditor name, `EndToEndId`) is stored in its own columns, `counterparty_name` and `end_to_end_id`. Re-extraction can then reuse it without the original file. When present, it takes precedence over the narration-based `counterparty_name_guess`.
- **Invoice patterns are per tenant** (`core.tenants.invoice_reference_patterns`). Changing them re-extracts the tenant's lines in a background job. `POST /api/core/maintenance/reextract` runs the same job on demand, for example after upgrading the extraction rules.
- **Narration prefixes are global** (`Core:Narration:Prefixes`). They only affect `normalized_narration`, never fingerprints (ADR 0005).
- **CAMT batch entries** (one entry with several `TxDtls`) are stored as **one line**. Their narration combines all the transactions' remittance info, and they carry no single counterparty or end-to-end ID. Splitting batches into separate lines is deferred until a real need appears. Doing so would change line identity for those entries.

## Consequences

- Phase 3 can use the GIN index on `extracted_refs` (e.g. `extracted_refs @> '{"invoiceRefs":["INV1041"]}'`) or filter candidates in memory.
- Rules can be added by registering another `IReferenceRule`. After deploying a new rule, run the re-extract job to apply it to existing lines.
