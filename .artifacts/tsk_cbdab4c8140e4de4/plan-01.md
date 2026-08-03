# Plan — Marketing `GetPagedAsync` IncludeDeleted flag is dead

## Summary

`MarketingActionRepository.GetPagedAsync` accepts an `IncludeDeleted` criterion but never bypasses the global EF Core soft-delete query filter (`!x.IsDeleted`) defined in `MarketingActionConfiguration.cs:89`. Requesting `includeDeleted=true` through the API silently returns only non-deleted rows. The fix makes the repository call `IgnoreQueryFilters()` when `IncludeDeleted` is true, mirroring the existing pattern already used in `GetByOutlookEventIdsAsync`, and adds a regression test proving the flag now works.

## Context

This is a code-review finding (`harness-issue:tsk_4fccc14d60904c6f:e42d71fa`), not a user-reported bug — no end user has filed a complaint, but the finding shows the capability is fully wired end-to-end (frontend hook → API request → handler → query criteria → repository) and looks implemented while being silently broken. Anyone building an audit/restore workflow on top of `includeDeleted=true` would get wrong results with no error or log signal. The codebase's soft-delete convention (per closed issue #2511) is: the global query filter is the single source of truth for exclusion, and any code path that needs deleted rows must explicitly call `IgnoreQueryFilters()`. `GetPagedAsync` violates that convention; `GetByOutlookEventIdsAsync` (same file, line 129) follows it correctly and is the reference implementation.

## Functional requirements

**FR-1 — `GetPagedAsync` honors `IncludeDeleted` by bypassing the global filter.**
When `criteria.IncludeDeleted == true`, the query against `MarketingAction` must call `.IgnoreQueryFilters()` so soft-deleted rows are eligible for return (subject to the other filters: search term, action type, date range, product code prefix).
- Acceptance: given a repository/DbContext seeded with 1 non-deleted and 1 soft-deleted (`IsDeleted = true`) `MarketingAction`, calling `GetPagedAsync` with `IncludeDeleted = true` returns both rows (`TotalCount == 2`).

**FR-2 — `GetPagedAsync` still excludes deleted rows by default.**
When `criteria.IncludeDeleted == false` (default), behavior is unchanged: only non-deleted rows are returned, whether that exclusion comes from the global filter alone or the existing manual `Where(x => !x.IsDeleted)` guard.
- Acceptance: same seed data as FR-1, calling `GetPagedAsync` with `IncludeDeleted = false` (or omitted) returns only the non-deleted row (`TotalCount == 1`).

**FR-3 — The two branches agree on enforcement mechanism (no redundant/contradictory filtering).**
Per the reviewer's suggested direction, resolve the branch so there's exactly one clear mechanism per branch — either rely on `IgnoreQueryFilters()` alone for the `IncludeDeleted == true` case (dropping the now-unreachable manual `Where` for that branch, which already only applies to the `false` branch) or keep the current structure but make it functionally correct. The manual `if (!criteria.IncludeDeleted) query = query.Where(x => !x.IsDeleted);` guard is actually redundant with the global filter today (per issue #2511's finding) — decide during implementation whether to leave it as defensive documentation-by-code or remove it, but the `true` branch must explicitly call `IgnoreQueryFilters()`.
- Acceptance: code review confirms no path exists where `IncludeDeleted = true` still silently drops deleted rows, and no path exists where `IncludeDeleted = false` unexpectedly returns deleted rows.

## Non-functional requirements

- **No behavior change for the default path.** `IncludeDeleted = false` is the default and by far the common case (calendar view, normal listing) — FR-2 must not regress it.
- **No performance concern.** `IgnoreQueryFilters()` only changes filter composition, not query cost profile beyond returning more rows when explicitly requested.
- **Test must exercise real EF Core filter evaluation**, not a mocked repository — the bug is specifically about global query filter interaction, which a `Mock<IMarketingActionRepository>` (as used in `GetMarketingActionsHandlerTests.cs`) cannot catch. Use `UseInMemoryDatabase` against `ApplicationDbContext` (per the existing pattern in `MeetingTranscriptRepositoryTests.cs` / `BankImportStateRepositoryTests.cs`) — EF Core's InMemory provider applies model-level `HasQueryFilter` the same way relational providers do, so no Testcontainers/Postgres dependency is needed here.

## Data model

No schema or entity changes. Existing entities/config involved:
- `MarketingAction` (domain entity) — has `IsDeleted` (bool).
- `MarketingActionConfiguration.cs:89` — global query filter `HasQueryFilter(x => !x.IsDeleted)` (unchanged).
- `MarketingActionQueryCriteria.cs:21` — `IncludeDeleted` (bool, default `false`) (unchanged).

## Interfaces

No API contract changes. Existing wiring stays as-is, it just starts working correctly:
- `frontend/src/api/hooks/useMarketingCalendar.ts:24,53` — `includeDeleted` param, already passed through.
- `GetMarketingActionsRequest.cs:20` — `IncludeDeleted` request field, already present.
- `GetMarketingActionsHandler.cs:35` — already maps `request.IncludeDeleted` → `criteria.IncludeDeleted`.
- `MarketingActionRepository.GetPagedAsync` (`backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:27-103`) — **only file needing a code change**.

## Dependencies and scope

**In scope:**
- `MarketingActionRepository.GetPagedAsync` — add `IgnoreQueryFilters()` on the `IncludeDeleted == true` branch.
- A new or extended repository-level test covering both `IncludeDeleted = true` and `= false`, using an InMemory `ApplicationDbContext` (no existing dedicated test file for `MarketingActionRepository` was found — likely add `backend/test/Anela.Heblo.Tests/Persistence/Marketing/MarketingActionRepositoryGetPagedTests.cs` or similar, following the `MeetingTranscriptRepositoryTests.cs` pattern).

**Out of scope:**
- `GetByIdAsync` and `GetForCalendarAsync` in the same repository — both hardcode `!x.IsDeleted` and never expose an include-deleted option; no finding was raised against them and changing their behavior is not requested.
- Any UI work for surfacing/restoring deleted marketing actions — the finding is about the query layer returning correct data when asked, not about building new UI to consume it.
- Reopening issue #2511's broader question of whether the manual soft-delete guards should be removed codebase-wide — only the `GetPagedAsync` inconsistency called out in this finding.

## Rough plan

1. **Fix**: In `GetPagedAsync`, change the `if (!criteria.IncludeDeleted) { query = query.Where(x => !x.IsDeleted); } ` block so the `IncludeDeleted == true` branch explicitly calls `query = query.IgnoreQueryFilters();` before other filters are applied — mirroring `GetByOutlookEventIdsAsync`. Check: read the diff, confirm the `true` branch calls `IgnoreQueryFilters()` and the `false` branch still excludes deleted rows (either via the global filter alone or the existing manual `Where`).
2. **Test**: Add a repository-level test using `UseInMemoryDatabase` seeding one non-deleted and one soft-deleted `MarketingAction`, asserting `GetPagedAsync(IncludeDeleted: true)` returns both and `GetPagedAsync(IncludeDeleted: false)` returns only the non-deleted one. Check: new test fails on the pre-fix code (red) and passes post-fix (green) — verify by running it against `git stash` of the fix if feasible, or by temporarily reverting locally.
3. **Validate**: Run `dotnet build` and the full Marketing test suite (`dotnet test --filter "FullyQualifiedName~Marketing"` or the project's standard test command) to confirm no regression in `GetMarketingActionsHandlerTests.cs` or other Marketing tests. Check: all tests green, `dotnet format` clean.

## Open questions

- **Should the redundant manual `Where(x => !x.IsDeleted)` guard in the `IncludeDeleted == false` branch be removed**, relying solely on the global filter (consistent with issue #2511's premise that manual guards are redundant), or left in place as-is since it's harmless and not the subject of this finding? Default: leave it in place — minimal, surgical fix; only touch the `true` branch. Flag for the implementer to confirm.
- **Test location/naming**: no existing `MarketingActionRepository`-specific test file was found. Default: create a new file `backend/test/Anela.Heblo.Tests/Persistence/Marketing/MarketingActionRepositoryGetPagedTests.cs` following the InMemory-provider pattern from `MeetingTranscriptRepositoryTests.cs`, since Marketing already has a `Persistence` folder precedent in the test project.
