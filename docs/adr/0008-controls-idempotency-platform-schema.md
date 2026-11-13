# 0008 — Phase 4: matching controls, idempotency and the platform schema

- **Status:** Accepted
- **Date:** 2026-11-13

## Decisions

1. **`SplitPayment` and `Tolerance` never auto-confirm.**
   - `IMatchingRule.CanAutoConfirm` is false for both. The engine caps them at Proposed whatever the thresholds are, enforcing §5.3's "always proposed" structurally rather than through score tuning.
   - **Scores:** SplitPayment 0.85 (+0.05 when every part carries the expected reference). Tolerance 0.75 (+0.10 for a reference, +0.05 for the name).
2. **The split search is a bounded subset-sum.**
   - **Where it looks:** parts are pre-filtered by direction, currency and window, and must each be smaller than the target. Combinations run from 2 to `maxItems` parts, and `maxCombinations` caps the nodes explored.
   - **When it proposes:** only when exactly one combination is found *and* the search completed. Two combinations mean `MultipleCandidates`. A search cut short by the budget proposes nothing, because uniqueness can't be proven.
   - **Both directions:** ManyToOne (an invoice paid in parts) and OneToMany (one payment covering several invoices).
3. **Tolerance.**
   - **Matching:** the difference (statement minus expected) is stored as `Match.Adjustment`, and the expected transaction is allocated in full, so it settles.
   - **Manual matches:** they may differ by the same tolerance, recorded the same way. Anything larger is refused with 422 `Match.Unbalanced`.
   - **Window:** Tolerance gains a `windowDays` parameter (default 3), which §5.3 didn't specify.
4. **Rejections stick.**
   - A pairing an Approver rejects is remembered by its item-set signature, and runs skip it. Proposals superseded by a rerun don't count, because nobody reviewed them.
   - An Approver can still create the same pairing manually.
5. **Maker-checker.**
   - For a manual match, `Match.Confirm` refuses (403 `Match.MakerChecker`) when the confirmer is the proposer, *including Admins*.
   - Engine proposals (proposer `system`) can be confirmed by any Approver.
   - Reversal needs an Approver and a reason, but not a second person.
6. **Bulk confirm** treats each match as its own unit of work, so one failure doesn't roll back the others. The response lists what was confirmed and why each failure failed.
7. **Idempotency (§3).**
   - **Scope:** `IdempotencyMiddleware` handles every `POST /api/**` carrying `Idempotency-Key`, keyed by (tenant, key). It stores a SHA-256 of method, path, query and body, and the response (up to 1 MB).
   - **Behaviour:**
     - a completed repeat replays the response, with `Idempotent-Replayed: true`;
     - a different request reusing the key gets 422;
     - a repeat while the first is still running gets 409;
     - responses of 500 or above are not stored, so the client can retry;
     - keys expire after 24 hours.
   - **Storage:** records live in a new **`platform`** schema, owned by BuildingBlocks rather than a module: idempotency is a host concern, and no module should own it. `PlatformDbContext` is a plain DbContext with no outbox, audit or tenant filter, and it filters by tenant explicitly. The Api migrates it along with the modules.
8. **Binding errors are 400, not 500.**
   - A malformed query value (e.g. an unknown enum value such as `status=open`) throws `BadHttpRequestException`. The global exception handler used to turn it into 500; it now uses the exception's own status code.
   - Enum query values remain **case-sensitive** (`status=Open`). The report export's `format` is the exception: it is parsed case-insensitively.

## Consequences

- Phase 5's UI must send enum values in their exact case, or the API should gain a case-insensitive binder.
- Old idempotency rows need periodic cleanup. They are ignored after 24 hours, and a cleanup job is a small follow-up.
