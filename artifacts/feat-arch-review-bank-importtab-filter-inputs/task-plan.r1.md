Plan complete and saved to `docs/superpowers/plans/2026-06-01-wire-bank-import-tab-filter-inputs.md` (1953 lines, 67 checkbox steps across 17 tasks).

**Summary of what the plan covers:**

- **Backend (Tasks 1–13):** Extend the request DTO + handler + repository to accept `transferId`/`account`/`dateFrom`/`dateTo`/`errorsOnly`. Implements ILike+LIKE-escape for both string filters via a new `LikeEscape` shared helper extracted from `PackageRepository`. Uses half-open date range (`>= from.Date && < to.Date.AddDays(1)`) per arch-review Decision 2. Adds five FluentValidation rules and **registers the `ValidationBehavior` for the Bank slice** (currently missing — without this the validator never runs). Catches `FluentValidation.ValidationException` explicitly in the controller so the generic 500 catch doesn't swallow it.
- **Backend tests (Tasks 9–12):** InMemory tests for date-range/errorsOnly filters; **separate Testcontainers PostgreSQL integration tests** for the ILike filters (since EF Core InMemory doesn't translate `EF.Functions.ILike` — the arch-review's "High" risk); validator unit tests; handler-level tests for trim+parse+forward.
- **Frontend (Tasks 14–16):** Extend the hand-rolled `useBankStatementsList` (not the generated client). Refactor `ImportTab.tsx` from seven scattered states into a single `committedFilters` object that drives React Query's `queryKey`. Inline client-side guard for `dateFrom > dateTo`. Component test mocks the hook and verifies submission payload + Vymazat reset.
- **Verification (Task 17):** OpenAPI regen, full BE + FE test suites, lint+format, plus the required UI smoke check in a browser per project rule.

A spec-coverage table at the bottom maps each FR/NFR + each arch-review recommendation back to the task(s) that implement it.