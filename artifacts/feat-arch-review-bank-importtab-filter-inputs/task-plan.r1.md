The implementation plan is already saved at `docs/superpowers/plans/2026-06-01-wire-bank-import-tab-filter-inputs.md` (1774 lines) and is complete. It contains:

- **Header** with goal, architecture summary, and tech stack
- **File Structure** mapping (7 backend files to modify, 2 new backend test files, 2 frontend files to modify, 1 new frontend test file)
- **14 tasks** decomposed into bite-sized TDD steps with exact code, commands, and expected output:
  1. Add filter fields to `GetBankStatementListRequest`
  2. Extend `IBankStatementImportRepository.GetFilteredAsync` signature
  3. Implement filter predicates in `BankStatementImportRepository` (ILike + EscapeLike + half-open date range)
  4. InMemory tests for `errorsOnly` and date-range predicates (5 tests)
  5. Testcontainers Postgres integration tests for `transferId`/`account` ILike (5 tests including `%` / `_` escape)
  6. Wire request fields through `GetBankStatementListHandler` (trim, parse dates)
  7. Extend `GetBankStatementListRequestValidator` (length, parseability, range)
  8. Register validator + `ValidationBehavior` in `BankModule` (closing the Risk 5 gap)
  9. Add `[FromQuery]` params and translate `ValidationException` → 400 in controller (closing the Risk 6 gap)
  10. Handler-level test (5 tests) covering 400 paths and trimmed/parsed forwarding
  11. Extend frontend `GetBankStatementListRequest` interface + URL serialization
  12. Refactor `ImportTab` to committed-filters object + inline `dateFrom > dateTo` error + filtrováno indicator update
  13. Frontend component test (4 tests) for hook payloads, trimming, validation block, and clear-resets-page
  14. Full validation suite (BE build/format, BE tests, FE build/lint/tests, OpenAPI regen check)
- **Self-Review** mapping every FR/NFR and every arch-review amendment to a specific task, plus a placeholder scan and type-consistency check.

Per the pipeline note, skipping the execution-choice prompt — the plan artifact is captured.