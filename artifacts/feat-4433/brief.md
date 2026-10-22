## Module / File
`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/ForceRefreshTask/ForceRefreshTaskHandler.cs`

## Coverage
Line coverage: 26% (5/19 lines, filter threshold: 60%)

## What's not tested
`Handle` has three return paths: (1) success → `{ Success: true }`; (2) `InvalidOperationException` from `ForceRefreshAsync` (task not found) → `{ NotFound: true, ErrorMessage: ... }`; (3) any other exception → `{ Success: false, ErrorMessage: "An unexpected error occurred..." }`. Only one of these paths is tested. Paths 2 and 3 return structurally different objects — a caller that does not check `NotFound` would treat a missing task as a generic failure, and a caller that checks `Success` only would fail to distinguish between the two error conditions.

## Why it matters
The frontend uses the response shape to decide what to display to the user. If the `NotFound` branch regresses (e.g. the exception type changes), a "task not found" scenario would silently return a generic error response, hiding the real cause.

## Suggested approach
Unit tests with a mock `IBackgroundRefreshTaskRegistry`: (1) successful refresh → `Success=true`; (2) `ForceRefreshAsync` throws `InvalidOperationException` → `NotFound=true` with message; (3) throws any other exception → `Success=false` with the fixed generic message. ~1h.

---
_Filed by weekly coverage-gap routine on 2026-10-05. Based on CI run #36876655065 (923c24940cf65d6ce38a8b56d75709bba333ca91)._
