# [coverage-gap] BackgroundRefresh/RunHydrationTierHandler: 4 response-shape branches never exercised

## Module / File
`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierHandler.cs`

## Coverage
Line coverage: 17.9% (filter threshold: 60%)

## What's not tested
The handler has four structurally distinct response paths, none of which are covered by any existing test:

1. **No enabled tasks in tier** → returns `{ NotFound = true, ErrorMessage = "No enabled tasks found for tier …" }`
2. **All tasks complete** → returns `{ TaskCount = N }`
3. **Cancellation** → catches `OperationCanceledException`, returns `{ Cancelled = true }`
4. **Unexpected exception** → catches `Exception`, returns `{ Success = false, ErrorMessage = "An unexpected error occurred during tier hydration" }`

None of these branches are exercised, so a typo in the flag names (`Cancelled`, `NotFound`, `TaskCount`) or a missing `await` on `ForceRefreshAsync` would ship silently.

## Why it matters
Callers (frontend and scheduled jobs) branch on `NotFound`, `Cancelled`, and `Success` to decide what to display. If a wrong field is set — e.g. `Cancelled = false` on a cancelled run — the UI silently shows "success" after the user cancels a hydration tier. The `NotFound` path is particularly risky: a misconfigured tier name passes through with no observable error.

## Suggested approach
Unit tests with a mocked `IBackgroundRefreshTaskRegistry`, covering:
- Empty tier (no tasks registered for that tier) → assert `NotFound == true`
- Successful 2-task tier → assert `TaskCount == 2`
- `CancellationToken` cancelled mid-loop → assert `Cancelled == true`
- `ForceRefreshAsync` throws → assert `Success == false` and message non-empty

~1–2 hours effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
