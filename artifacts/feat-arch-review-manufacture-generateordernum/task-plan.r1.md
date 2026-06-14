Plan saved to `docs/superpowers/plans/2026-06-13-manufacture-generate-order-number-supplied-year.md`.

**Summary:** 8 tasks across two commits.
- **Commit 1 (refactor, Tasks 1–4):** thread `int year` through the repository interface and implementation, cache `var now = _timeProvider.GetUtcNow()` once in both handlers and pass `now.Year` to the repo, reuse `now.DateTime` for `CreatedDate` / `StateChangedAt` / expiration / lot, and mechanically update the 18 existing mock signatures across three test files. Existing green suite acts as the safety net.
- **Commit 2 (guards, Tasks 5–8):** six new xUnit tests — year-end and year-start cases for both handlers (FR-3), plus `SetupSequence` audit-consistency tests that fail if the handler reads `TimeProvider` more than once (arch review R-2 mandatory clarification). Closes with full-suite run, `dotnet format`, and a grep guard verifying zero clock reads remain inside `GenerateOrderNumberAsync`.

Spec coverage is mapped requirement-by-requirement in the Self-Review section; no placeholders; signatures and helper names verified against the live code.