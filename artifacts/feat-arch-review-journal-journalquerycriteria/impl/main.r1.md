Now writing the implementation output artifact.

---

# Implementation: Remove Journal Query/Search Criteria from Domain Layer

## What was implemented

Eliminated `JournalQueryCriteria` and `JournalSearchCriteria` from the Domain layer in a pure backend refactor. `IJournalRepository` now accepts primitive query parameters directly (matching the convention already used by `ILotRepository` / `IEanRepository`). Both MediatR handlers pass `request.*` fields through to the repository with no intermediate translation step. The `SearchJournalEntriesHandler` uses named arguments at the 11-parameter call site to guard against silent argument-reordering bugs in future maintenance. LINQ behavior is preserved 1:1. Public API contracts are untouched.

## Files created/modified

**Deleted (Domain):**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs` — removed entirely
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs` — removed entirely

**Modified:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — `GetEntriesAsync` and `SearchEntriesAsync` now accept primitive parameters; other methods unchanged
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — implementations updated; LINQ sort/filter/include behavior preserved 1:1
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` — removed `JournalQueryCriteria` allocation; passes request fields directly
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — removed `JournalSearchCriteria` allocation; uses named arguments on 11-param call
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — all Moq setups updated to per-parameter matchers; `Verify(...)` asserts `productCodePrefix == "TON002"` at position 4

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — 55 Journal tests, all passing (same count as baseline)
- No tests deleted; test intent fully preserved
- `SearchByProductCodePrefix_ShouldReturnEntriesWithMatchingPrefix` correctly asserts `productCodePrefix` flows through via `It.Is<string?>(p => p == "TON002")` at position 4

## How to verify

```bash
# From repo root:

# Build (0 warnings, 0 errors)
dotnet build Anela.Heblo.sln --nologo

# Format (clean, no drift)
dotnet format Anela.Heblo.sln --verify-no-changes

# Tests (55 passing)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Journal" --nologo

# FR-1: No criteria type references in src or test
grep -rn "JournalQueryCriteria\|JournalSearchCriteria" backend/src backend/test

# NFR-2: Domain has no Application references
grep -R "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain

# FR-7: API contracts untouched
git diff --name-only 8dc8a75a...HEAD -- backend/src/Anela.Heblo.Application/Features/Journal/Contracts/
```

## Notes

- Named arguments on `SearchEntriesAsync` call site in `SearchJournalEntriesHandler` are required (11 parameters of similar types — positional invocation forbidden per arch-review amendment 1)
- `System.ComponentModel.DataAnnotations` remains in Domain transitively (used by other domain entities); no `.csproj` changes were needed
- The `productCodePrefix.StartsWith(pa.ProductCodePrefix)` direction in the filter is intentionally counter-intuitive (request value starts with stored prefix) — preserved exactly as-is

## PR Summary

Removes `JournalQueryCriteria` and `JournalSearchCriteria` from the Domain layer, which carried pagination, sort directives, and `[MaxLength]` data annotations — Application/infrastructure concerns that had no business in Domain.

`IJournalRepository` now exposes `GetEntriesAsync` and `SearchEntriesAsync` with primitive parameter lists, bringing it in line with the existing `ILotRepository` / `IEanRepository` convention. Handlers pass `request.*` fields straight through; the dead criteria-construction translation step is deleted. Named arguments are used at the 11-parameter search call site to defend against silent reordering during future maintenance.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — new primitive-parameter signatures
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs` — deleted
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs` — deleted
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — updated to match new interface; LINQ behavior unchanged
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` — simplified, no criteria object
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — simplified, named arguments
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — per-parameter Moq matchers

## Status
DONE