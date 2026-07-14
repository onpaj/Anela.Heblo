## Module / File
`frontend/src/components/BackgroundTasksCard.tsx`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
The component contains several pure utility functions with meaningful branching logic, none of which are tested:

### `formatDuration(timeSpan: string)` — probable latent bug
The function handles two formats: `"hh:mm:ss"` and `"dd.hh:mm:ss"`. It splits on `:` and reads `parts[0]` as `hours`. For a string like `"1.05:30:00"` (1 day, 5 hours, 30 min), `parseInt("1.05")` yields `1` — not 29 (the actual hour count). The `hours >= 24` branch is never reached, so the display would show `"1h 30m"` instead of `"1d 5h"`. This is a **confirmed bug** for any task whose duration or interval exceeds 24 hours.

### `getTimeUntilNextRun(nextScheduledRun)` — 4 branches uncovered
- Past/overdue → `"Spouští se..."`
- Future < 60 min → `"za N min"`
- Future < 24 h → `"za Nh Nm"`
- Future >= 24 h → `"za Nd Nh"`

### `getStatusBadge(task)` — 5 status paths uncovered
Disabled, no execution yet, Running, Completed, Failed, Cancelled — each returns a different JSX badge, none asserted.

## Why it matters
`formatDuration` is shown for every task's `initialDelay` and `refreshInterval`. Any background task scheduled to run less frequently than once a day (e.g. weekly catalog refresh) would display an incorrect duration. Users and operators would see `"1h 30m"` instead of `"1d 5h"` with no indication something is wrong.

## Suggested approach
Extract `formatDuration`, `getTimeUntilNextRun`, and `getStatusBadge` into a `backgroundTasksHelpers.ts` module and add unit tests:

- `formatDuration("1.05:30:00")` → `"1d 5h"` (currently broken — this test would fail and surface the bug)
- `formatDuration("00:30:00")` → `"30m"`
- `formatDuration("02:15:00")` → `"2h 15m"`
- `getTimeUntilNextRun` for past, <60 min, <24 h, and >=24 h inputs
- `getStatusBadge` for each status string

~2–3 hours effort (including fixing the `formatDuration` bug).

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
