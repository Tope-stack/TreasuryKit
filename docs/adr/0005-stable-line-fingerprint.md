# 0005 — Line fingerprints use a fixed, minimal normalization

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

Plan §4.4 defines the statement-line fingerprint as `SHA256(accountId | valueDate | amount | bankReference ?? normalizedNarration | occurrenceIndex)`. Phase 2 introduces a richer `INarrationNormalizer`: it strips bank prefixes, removes punctuation noise, and its prefix list is configurable. If the fingerprint used that normalizer, then any change to it would alter the fingerprints of lines already stored. A re-imported or overlapping statement would then insert those lines again as duplicates. The duplicate protection would silently stop working.

## Decision

- The fingerprint uses `TextNormalization.ForFingerprint`: trim, upper-case (invariant culture), collapse whitespace. **This function and the fingerprint input format must never change.**
- The input is `accountId:N | yyyy-MM-dd | amount:F4 | ("R:" + reference) or ("N:" + narration) | occurrenceIndex`. The `R:`/`N:` prefix stops a reference that happens to equal some narration from colliding with it.
- `occurrenceIndex` counts identical (valueDate, amount, identity) tuples within one file, in file order. Two genuinely identical charges on the same day therefore both survive. The same pair in an overlapping file maps to the same two fingerprints.
- The richer Phase 2 normalization is stored separately and used only for matching. It does not affect identity.

## Consequences

- If a bank changes its narration formatting between exports, the same transaction can produce a different fingerprint and be inserted twice. A bank reference, when present, avoids this. The Phase 2 parsers should populate `BankReference` wherever the format carries one (MT940 `:61:` reference, CAMT `AcctSvcrRef`).
- If the fingerprint ever has to change, it needs a data migration that recomputes fingerprints for all stored lines.
