# Implementation: domain-isalldy-property

## What was implemented

Added a persisted `IsAllDay` boolean to the `MarketingAction` domain entity,
threaded it through the constructor and `UpdateDetails`, and added a static
`ComputeIsAllDay(DateTime, DateTime?)` helper that reproduces the existing
midnight-to-midnight guess for callers that have no more authoritative
source (Graph's own `isAllDay` flag). `Reschedule` was left untouched per
architect Decision 2 (a move never changes all-day-ness) and got an XML-doc
comment explaining the omission.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — added the `IsAllDay` property (private set), the `isAllDay` constructor/`UpdateDetails` parameter, the static `ComputeIsAllDay` helper, and an XML-doc comment on `Reschedule`.
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionTestBuilder.cs` — added `_isAllDay` backing field, `WithIsAllDay` fluent setter, threaded `isAllDay: _isAllDay` through both the constructor and `UpdateDetails` calls in `Build()`.
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs` — added `isAllDay: false,` to all 6 existing `new MarketingAction(...)` call sites; added `Ctor_SetsIsAllDayExactlyAsPassed` and the theory `ComputeIsAllDay_MatchesTheMidnightToMidnightRule`.
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs` — added `isAllDay: false,` to all 7 existing `action.UpdateDetails(...)` call sites; added `UpdateDetails_SetsIsAllDayExactlyAsPassed`.

## Tests

- `MarketingActionConstructorTests.Ctor_SetsIsAllDayExactlyAsPassed` — constructor sets `IsAllDay` exactly as passed.
- `MarketingActionConstructorTests.ComputeIsAllDay_MatchesTheMidnightToMidnightRule` (3 cases) — the static helper reproduces the old midnight-to-midnight guess, including the "midnight start, no end" edge case (`false`).
- `MarketingActionUpdateDetailsTests.UpdateDetails_SetsIsAllDayExactlyAsPassed` — `UpdateDetails` sets `IsAllDay` exactly as passed.
- All pre-existing tests in both files updated to pass `isAllDay: false` (their prior implicit default) so behavior is unchanged for them.

## How to verify

```bash
cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Builds clean, 0 warnings, 0 errors.

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionConstructorTests|FullyQualifiedName~MarketingActionUpdateDetailsTests"
```
Currently fails to **compile** the test project — but only with the 4
errors expected and called out by the task plan's own dependency notes:
`CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs`, and
`OutlookEventImportMapper.cs` (2 call sites) still call the old
constructor/`UpdateDetails` overloads without `isAllDay`. These three files
are explicitly out of this task's file scope and are the listed
responsibility of the dependent tasks `import-mapper-isalldy` and
`manual-handlers-isalldy` (both declared as depending on this task in
`task-plan.r1.md`). No compile error originates from any file this task
touched — confirmed by isolating the `Anela.Heblo.Domain` project build
above (clean) and by inspecting the full error list (exactly 4 errors, all
in the three named out-of-scope files).

## Notes

This is task 1 of 5 in a sequential plan; the branch is intentionally left
non-green at the whole-solution/whole-test-project level until the
dependent tasks land. This matches the task-plan's own stated dependency
structure ("Depends on task: domain-isalldy-property") — `persistence-migration-isalldy`,
`import-mapper-isalldy`, `export-service-isalldy`, and `manual-handlers-isalldy`
all declare a dependency on this task, not the reverse, and `manual-handlers-isalldy`
Step 4 is explicitly the point where the plan expects `dotnet build` to go
fully green across the solution.

## PR Summary
Added a persisted `IsAllDay` boolean to `MarketingAction`, replacing the plan for deriving all-day-ness with an explicit, domain-owned flag set by every constructor/`UpdateDetails` call. Added a shared `ComputeIsAllDay` static helper that reproduces the existing midnight-to-midnight guess for callers with no more authoritative source. `Reschedule` is intentionally untouched (a move never changes all-day-ness), now documented with an XML comment.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — added `IsAllDay` property, constructor/`UpdateDetails` parameter, `ComputeIsAllDay` static helper
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionTestBuilder.cs` — added `WithIsAllDay` fluent setter
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionConstructorTests.cs` — updated all call sites, added 2 new test cases (one a 3-case theory)
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionUpdateDetailsTests.cs` — updated all call sites, added 1 new test case

## Status
DONE
