All 13 tasks complete. Here is the output artifact:

---

# Implementation: Decouple GridLayouts Application Handlers from Npgsql

## What was implemented

Removed the direct Npgsql dependency from the three GridLayouts MediatR handlers by:
1. Adding a `GridLayoutPersistenceException` domain exception with a nullable `SqlState` string property so handlers can preserve their `{SqlState}` log field without importing Npgsql.
2. Adding a `PostgresExceptionTranslator` static class in `Persistence/Infrastructure` that mirrors the recursive unwrap pattern from `PostgresExceptionLoggingInterceptor`, catching direct `NpgsqlException`, `DbUpdateException` wrapping `NpgsqlException`, and returning `null` for all other exception types (pass-through).
3. Wrapping all three `GridLayoutRepository` methods with the translator.
4. Updating all three handlers to catch `GridLayoutPersistenceException` instead of the Npgsql union type.
5. Updating all three handler test files to throw and assert `GridLayoutPersistenceException`.
6. Adding XML `<exception>` doc tags to `IGridLayoutRepository`.

## Files created/modified

**Created:**
- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayoutPersistenceException.cs` — domain exception with `SqlState` property (FR-1, amended shape)
- `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — static translator wrapping Npgsql → domain exception (FR-2 support)
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/PostgresExceptionTranslatorTests.cs` — 5 unit tests for the translator
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` — 4 repository-level tests using `ThrowingApplicationDbContext` (FR-8)

**Modified:**
- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — wrapped GetAsync, UpsertAsync, DeleteAsync with translator; inlined internal calls to avoid double-wrapping
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — removed `using Npgsql;`, catches `GridLayoutPersistenceException`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — same
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` — same
- `backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs` — added `<exception>` XML docs
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — replaced `NpgsqlException` with `GridLayoutPersistenceException`
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` — same
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs` — same

## Tests

- `PostgresExceptionTranslatorTests.cs` — 5 tests: direct NpgsqlException, DbUpdateException wrapping, OperationCanceledException passthrough, DbUpdateConcurrencyException passthrough, generic exception passthrough
- `GridLayoutRepositoryTranslationTests.cs` — 4 tests: UpsertAsync NpgsqlException, UpsertAsync DbUpdateException wrapping, UpsertAsync non-Pg passthrough, DeleteAsync NpgsqlException
- Handler tests (3 files) — 7 tests updated to use `GridLayoutPersistenceException`
- **Total: 16 GridLayout tests, all passing**

## How to verify

```bash
# Grep check (FR-6 reinterpretation)
grep -r "using Npgsql" backend/src/Anela.Heblo.Application/Features/GridLayouts
# Expected: no output

# Build
dotnet build Anela.Heblo.sln
# Expected: 0 errors

# Format
dotnet format Anela.Heblo.sln --verify-no-changes
# Expected: exit code 0

# All 16 GridLayout tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GridLayout" --no-build
# Expected: 16 passed
```

## Notes

**Deviations from original spec (all per arch-review amendments):**
- `GridLayoutPersistenceException` constructor takes three args `(string, string?, Exception)` not two — needed to preserve `{SqlState}` structured log field in handlers without re-importing Npgsql (Spec Amendment 1).
- `PostgresExceptionTranslator` is `public` not `internal` — no `InternalsVisibleTo` pattern existed in the project, so public visibility was chosen per the plan's decision rule.
- FR-6 satisfied by grep verification only (no csproj change) — no direct `<PackageReference Include="Npgsql" />` exists in Application.csproj; Npgsql arrives transitively via Persistence → `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `DbUpdateException` wrapping `PostgresException` is now also translated — this was a **latent bug fix**: the original catch `when (ex is PostgresException or NpgsqlException)` did not catch EF Core save-path constraint violations. Call this out in the PR.

**Pre-existing quality concerns surfaced by code reviewer (not introduced by this PR, out of scope):**
- `GetGridLayoutHandler`: `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto()` silently swallows malformed JSON — pre-existing, separate ticket needed
- `SaveGridLayoutHandler`: `JsonSerializer.Serialize(payload)` not wrapped — pre-existing, separate ticket needed
- Three other Application files still import Npgsql (`Photobank/PhotobankRepository.cs`, `PackingMaterials/ConsumptionCalculationService.cs`, `Smartsupp/.../ProcessWebhookEventHandler.cs`) — explicitly out of scope per spec

## PR Summary

Removes the direct Npgsql dependency from all three GridLayouts MediatR handlers (Get/Save/Reset), restoring Clean Architecture's dependency direction (Application → Domain only). The fix introduces a `GridLayoutPersistenceException` domain type with a nullable `SqlState` property so handlers keep their existing `{SqlState}` log field, a static `PostgresExceptionTranslator` in the Persistence layer that mirrors the existing `PostgresExceptionLoggingInterceptor` unwrap recursion, and wraps all three `GridLayoutRepository` methods at the persistence boundary.

As a deliberate side-effect, the repository wrapper now also catches `DbUpdateException` wrapping a `PostgresException` (constraint violations from `SaveChangesAsync`), which the original handler `catch (Exception ex) when (ex is PostgresException or NpgsqlException)` silently missed — this is a latent bug fix, not a silent behavior change.

### Changes
- `GridLayoutPersistenceException.cs` — new domain exception with `SqlState` string property
- `PostgresExceptionTranslator.cs` — new static helper in Persistence/Infrastructure translating Npgsql family exceptions to the domain type
- `GridLayoutRepository.cs` — wraps GetAsync/UpsertAsync/DeleteAsync; inlines previously-delegated GetAsync calls to avoid double-wrapping
- `GetGridLayoutHandler.cs`, `SaveGridLayoutHandler.cs`, `ResetGridLayoutHandler.cs` — removed `using Npgsql;`, catches `GridLayoutPersistenceException`
- `IGridLayoutRepository.cs` — added `<exception>` XML doc tags
- Handler tests (3) — replaced `NpgsqlException` with `GridLayoutPersistenceException` in mock setups and logger verifications
- New translator unit tests (5) and repository-level integration tests (4) using `ThrowingApplicationDbContext`

## Status
DONE_WITH_CONCERNS

Concerns: Code quality reviewer identified 2 HIGH-severity pre-existing issues in unchanged code (`JsonSerializer` error handling gaps in Get and Save handlers). These were intentionally left unfixed to keep this PR surgical (spec NFR-1: zero behavioral changes). Follow-up tickets should address them separately.