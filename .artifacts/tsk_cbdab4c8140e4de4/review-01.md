# Review — Marketing `GetPagedAsync` IncludeDeleted flag is dead

## Verdict: done

## What was checked

Read `plan-01.md`, `design-01.md`, `architecture-01.md`, `development-01.md`, and the actual diff (`git show HEAD`) against the original finding.

## Diff summary

`backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`:

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

replacing the single-branch `if (!criteria.IncludeDeleted) { query = query.Where(x => !x.IsDeleted); }`.

Plus a new test file `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs` with two tests: one seeding a non-deleted + a soft-deleted `MarketingAction` and asserting `IncludeDeleted=true` returns both, the other asserting `IncludeDeleted=false` returns only the non-deleted one.

## Conformance to spec / finding

- Directly fixes the reported bug: `IncludeDeleted=true` now calls `IgnoreQueryFilters()`, bypassing the global `!x.IsDeleted` filter from `MarketingActionConfiguration.cs`, exactly as the finding's "Suggested direction" specified.
- Mirrors the existing reference pattern in `GetByOutlookEventIdsAsync` (same file), as required.
- `IgnoreQueryFilters()` is applied before all the other composed filters (search term, action type, date range, product prefix), so it doesn't interact incorrectly with the rest of the query — confirmed by reading the full method body in the current source.
- Scope is surgical: only the one conditional block changed in production code, no other repository methods touched, no API/contract changes, matching the plan/design/architecture docs.

## Correctness verification (ran independently, not just trusting development-01.md's claims)

- `dotnet build` on the full solution — 0 errors, 251 pre-existing warnings, none newly introduced.
- `dotnet test --filter "FullyQualifiedName~MarketingAction"` — 134/134 passed, including the 2 new tests.
- `dotnet format --verify-no-changes` — clean, no output, exit 0.

The two new tests are meaningful (not tautological): they seed both a deleted and non-deleted row and assert on `TotalCount`, which only passes if the global filter is actually bypassed for the `IncludeDeleted=true` case. `development-01.md` also documents a manual red/green check (reverting the fix broke the true-branch test), which is consistent with the fix being load-bearing.

## Assessment

The implementation is a minimal, correct, well-tested fix that resolves the exact defect described in the finding, follows the codebase's own established pattern, and required no architectural changes. No functional requirement is unmet, no correctness issue found, no missing test coverage.
