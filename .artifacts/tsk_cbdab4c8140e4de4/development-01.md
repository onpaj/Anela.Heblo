# Development — Marketing `GetPagedAsync` IncludeDeleted flag is dead

## Summary

Implemented the fix and test exactly as specified in `plan-01.md` / `design-01.md`, approved without changes in `architecture-01.md`.

## Changes

### `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`

In `GetPagedAsync`, replaced:

```csharp
if (!criteria.IncludeDeleted)
{
    query = query.Where(x => !x.IsDeleted);
}
```

with:

```csharp
if (criteria.IncludeDeleted)
{
    query = query.IgnoreQueryFilters();
}
else
{
    query = query.Where(x => !x.IsDeleted);
}
```

`IgnoreQueryFilters()` is applied as the very first operation on `query`, before the search/type/date/product filters are composed, mirroring the reference pattern already used in `GetByOutlookEventIdsAsync` (same file, line 129). No other method in the class was touched.

### `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs` (new)

New test class following the `MeetingTranscriptRepositoryTests.cs` pattern (`Repositories/` folder, InMemory `ApplicationDbContext`, constructor-injected repository, `IDisposable`):

- `GetPagedAsync_IncludeDeletedTrue_ReturnsBothRows` — seeds one non-deleted + one soft-deleted `MarketingAction`, asserts `IncludeDeleted = true` returns `TotalCount == 2`.
- `GetPagedAsync_IncludeDeletedFalse_ReturnsOnlyNonDeleted` — same seed, asserts `IncludeDeleted = false` returns `TotalCount == 1` and the single item is non-deleted.
- Seeding uses the domain constructor + `SoftDelete("seed-user", "Seeder", DateTime.UtcNow)`, using `MarketingActionType.Blog` (confirmed real enum member; the design doc's `Campaign` placeholder was corrected per `architecture-01.md`'s note).

Manually verified red/green: reverting the fix (restoring the pre-fix `if (!criteria.IncludeDeleted)` single-branch code) makes `GetPagedAsync_IncludeDeletedTrue_ReturnsBothRows` fail (`TotalCount == 1` instead of `2`), confirming the test actually exercises the bug; re-applying the fix makes both tests pass.

## Verification

- `dotnet build` (full solution) — 0 errors, 251 pre-existing warnings (none introduced by this change), succeeded.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingAction" --no-build` — **134/134 passed**, including the 2 new tests. No regression in `GetMarketingActionsHandlerTests.cs` or other Marketing tests (handler tests mock the repository, so they're unaffected by this change as predicted in the architecture review).
- `dotnet format Anela.Heblo.sln --no-restore --verify-no-changes` — clean, no formatting changes needed.

## How to verify

```bash
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
cd <repo root>
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingAction"
dotnet format Anela.Heblo.sln --no-restore --verify-no-changes
```

## Scope

Exactly as planned: one production file changed (`MarketingActionRepository.cs`, the `if/else` block in `GetPagedAsync`), one new test file. No frontend changes, no API contract changes, no other repository methods touched, no migration.
