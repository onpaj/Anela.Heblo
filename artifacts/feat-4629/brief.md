## Module / File
`frontend/src/api/hooks/useBackgroundRefresh.ts`

## Coverage
Line coverage: 0% (0/67 lines). Filter threshold: 60%

## What's not tested
The `!response.ok` throw path is uncovered for all five fetch functions. `forceRefreshTask` calls `response.json()` on error without a `.catch`, making it inconsistent with `runHydrationTier` (which safely uses `.catch(() => ({}))`)) — a non-JSON error response would mask the original HTTP error. All five query functions' success paths are also dark.

## Why it matters
This is the custom raw HTTP layer for background task control (force-refresh, hydration tiers). An unhandled rejection from a non-JSON error body would surface as an unexpected crash in the UI rather than a user-readable error message, and the inconsistency between error handlers is a silent footgun.

## Suggested approach
Unit tests with a mocked `http.fetch` covering success and `!response.ok` paths for `forceRefreshTask` and `runHydrationTier`; verify error message extraction matches the expected format and that the error handling is consistent across all five functions. Estimated effort: ~2 hours.

---
_Filed by weekly coverage-gap routine on 2026-11-09. Based on CI nightly run #42456500800 (16643224c30a67e613312ac689cd872155b43bd1)._
