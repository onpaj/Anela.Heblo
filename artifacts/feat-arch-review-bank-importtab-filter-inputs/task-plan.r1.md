Plan saved to `docs/superpowers/plans/2026-06-03-bank-importtab-filter-inputs.md`.

Summary of the plan:

- **15 tasks** decomposed by layer, each broken into TDD substeps (write failing test → run → implement → re-run → commit).
- **Task 1 is a prerequisite gate**: the existing `GetBankStatementListRequestValidator` is not currently registered with DI, so it won't run until `BankModule.cs` wires the validator + `ValidationBehavior`, and the controller catches `FluentValidation.ValidationException` for 400 responses.
- **Tasks 2–3** introduce the `BankStatementListFilter` domain record and migrate `IBankStatementImportRepository.GetFilteredAsync` to take it (plus `CancellationToken`), leaving existing behaviour intact and existing tests green.
- **Tasks 4–7** add the four new repository filter clauses with TDD: `ErrorsOnly` (uses `ImportStatus.Success` constant) and date range are testable on the InMemory provider; `TransferId` / `Account` `EF.Functions.ILike` matching ships with the `EscapeLike` helper copied from `PackageRepository` and is covered by a new Testcontainers-Postgres integration test class.
- **Tasks 8–10** extend the request DTO, handler (parses `string?` → `DateTime?`, trims string filters), and validator (length cap 100, parseable dates, `DateFrom <= DateTo`).
- **Task 11** exposes the five new `[FromQuery]` parameters on the controller action.
- **Tasks 12–14** wire the frontend hook DTO and serialisation, refactor `ImportTab` to use a `committedFilters` state object (kills the `refetch()` no-op pattern), surfaces an inline `dateFrom > dateTo` error, updates the `(filtrováno)` indicator to be honest about all five filters, and ships a component test.
- **Task 15** runs the full validation gate from `CLAUDE.md`.

The plan's self-review section traces each FR/NFR from the spec and each amendment from the arch-review to specific tasks.