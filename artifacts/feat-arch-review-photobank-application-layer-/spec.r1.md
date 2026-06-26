I've written the specification to `artifacts/feat-arch-review-photobank-application-layer-/spec.md`, grounded in the actual code.

# Specification: Remove Npgsql dependency from Photobank Application layer

## Summary
`GetPhotosHandler` imports `Npgsql` and catches `PostgresException` (SQL state `2201B`) to convert a bad user regex into a structured error — a Clean Architecture layer leak. The fix introduces a domain exception `InvalidPhotoSearchPatternException`, translates the Npgsql exception inside `PhotobankRepository` (the infrastructure seam), and has the handler catch the domain exception, preserving the exact client-facing contract.

## Functional Requirements
- **FR-1** — New `InvalidPhotoSearchPatternException` in `Domain/Features/Photobank/`, carrying `Pattern`; no new package refs on Domain (follows the existing `IssuedInvoiceClientException` convention).
- **FR-2** — `PhotobankRepository.GetPhotosAsync` wraps query execution and rethrows `PostgresException` with `SqlState == "2201B"` (when `useRegex`) as the domain exception. Other `BuildFilterQuery` callers (non-regex) untouched.
- **FR-3** — Handler drops `using Npgsql;`, catches `InvalidPhotoSearchPatternException`, returns the identical response (`Success=false`, `PhotobankInvalidRegexPattern`, `Params["pattern"]`).
- **FR-4** — New `GetPhotosHandlerTests` case mocking the repo to throw the domain exception (now unit-testable without a DB).

## Key findings driving the spec
- `PhotobankRepository` lives in the **Application** project but is the data-access adapter (references EF Core + Persistence), making it the correct translation point.
- The error path is **currently untested** — `PhotobankRepositoryFilterTests` uses the EF Core **in-memory** provider, which never raises `PostgresException 2201B`, confirming the brief's "silently stops working" concern.
- Npgsql stays available to the repository transitively via `Anela.Heblo.Persistence`; no new NuGet packages needed.

I marked the spec **COMPLETE** — the refactor is fully determined by the brief and the codebase, with no open questions. Out-of-scope items (relocating the repository to a dedicated infra project; the same anti-pattern in `GridLayouts`/`ConsumptionCalculationService`) are noted explicitly.