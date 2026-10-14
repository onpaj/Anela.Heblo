# Implementation: add-reschedule-domain-method

## What was implemented

Added a dedicated `Reschedule` domain method to `MarketingAction` that updates
`StartDate`, `EndDate`, and the modification audit fields (`ModifiedAt`,
`ModifiedByUserId`, `ModifiedByUsername`) without touching `Title`,
`Description`, or `ActionType` — replacing the previous approach of calling
`UpdateDetails` with the entity's own unchanged values for a date-only move.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — added
  `Reschedule(DateTime startDate, DateTime? endDate, string modifiedByUserId,
  string? modifiedByUsername, DateTime utcNow)` immediately before `UpdateDetails`,
  in the "Domain methods" section. No other method was changed.
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs`
  — new test file with 9 facts covering: start/end date update, null end date,
  title/description/actionType left unchanged, `ModifiedAt` set from `utcNow`,
  `ModifiedByUserId`/`ModifiedByUsername` set from arguments, default to
  "Unknown User" when `modifiedByUsername` is null, created-audit fields
  untouched, deletion/Outlook fields untouched, and product associations/folder
  links untouched.

## Tests

- `MarketingActionRescheduleTests.cs` (new, 9 facts) — all passing.
- Ran `dotnet test ... --filter "FullyQualifiedName~MarketingActionRescheduleTests"`
  → Passed: 9, Failed: 0.
- Ran `dotnet test ... --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Marketing"`
  (full domain Marketing suite, regression check) → Passed: 65, Failed: 0 — no
  existing tests broke.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Marketing"
```

## Notes

This task only adds the new `Reschedule` method to the domain entity; it does
not yet switch `MoveMarketingActionHandler` to call it — that is the separate
`switch-handler-to-reschedule` task per the task plan. `UpdateDetails` itself
was left untouched, as instructed.

## PR Summary

Added a focused `Reschedule` domain method to `MarketingAction` so a date-only
move no longer has to go through `UpdateDetails` with the entity's own
unchanged `Title`/`Description`/`ActionType` values. Covered with 9 new unit
tests; the full domain Marketing test suite (65 tests) still passes.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — added `Reschedule` method
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs` — new test file (9 facts)

## Status
DONE
