# Logeto break insertion — rolling window and same-day handling

Amends `2026-08-05-logeto-break-insertion-design.md`. That document stays the
reference for everything not restated here (worker selection, slot placement, merge
semantics, time representation). Only the **date range** of the daily walk and the
handling of **unfinished days** change.

## Problem

Two limits in the shipped `BreakInsertionService` cause days to be missed or work to
be repeated:

1. **The walk has no upper bound on cost.** It scans every day from a fixed
   `StartDate` (2026-08-01) to yesterday, on every run, forever. In six months that is
   a ~180-day scan each night to insert at most one or two breaks.
2. **Today is never processed** (`lastDay = today - 1`). A day is only ever considered
   once its calendar day has ended, and a day skipped for a transient reason is
   re-examined on later runs only because the walk happens to be unbounded — which is
   the very property being removed in (1).

## Requirements

### R1 — Rolling window with a hard floor

The walk covers `[from, to]` where:

```
today = current date, Europe/Prague
from  = max(StartDate, today - LookbackDays)
to    = today
```

- `LookbackDays` is new configuration, default `7`. The window therefore spans
  `LookbackDays` days of history **plus today** — 8 calendar days at the default.
- `StartDate` keeps its current meaning and value (2026-08-01): an absolute floor the
  walk never crosses, regardless of `LookbackDays`.
- If `from > to` (floor set in the future), the run logs and returns an empty summary
  **without calling the Logeto API**.
- The per-person day filter inside the service uses the same `from`/`to` bounds as the
  `GetTimeTrackingAsync` call — the two must not diverge.

### R2 — Today is in scope; unfinished days are never touched

A day is **in progress** when any of its entries has `From` set and `To` null — the
open record Logeto holds while a worker is clocked in. Such a day is skipped entirely:
no break is inserted, no work record is split.

- The check runs **first** in the per-day rules, before the existing-break check.
- It applies to **every** day in the window, not only today. An open record on a past
  day means the day's data is unusable, whichever day it is.
- Hours-only records (`From` and `To` both null) are unaffected and keep their existing
  handling — they are not "in progress".
- New summary counter `SkippedInProgress`, included in the run summary log line.

Log severity splits by date, because the same condition means two different things:

| Day | Meaning | Level |
|---|---|---|
| `date == today` | Worker is currently at work. Expected. | Debug |
| `date < today` | Worker never clocked out. The day will never get a break until a human fixes it in Logeto. | Warning |

**Assumption to verify against live data:** that Logeto represents a running record as
`From` set / `To` null in `GET /TimeTracking`. If it instead omits running records from
the response, the design degrades safely rather than breaking — the day looks empty,
falls below the 6 h threshold, and is skipped; once the worker clocks out the record
appears closed and the next run inserts the break. Either way no break lands inside an
unfinished shift. Confirm the actual shape during rollout and record it in the spike
results document.

### R3 — One break per day, including split shifts

Unchanged behaviour, stated here because it must not regress: a 12 h day worked as two
~6 h shifts receives **exactly one** break. `ComputeBreakSlot` returns a single
`TimeSlot?` and `ProcessDayAsync` performs at most one insert per day, so more than one
break per day is not reachable. This is currently covered only at the
`BreakSlotCalculator` level; R3 adds the missing service-level assertion.

## Why the schedule stays nightly

The job keeps `CronExpression = "0 3 * * *"`. At 03:00 today normally has no entries at
all, so R2 rarely fires on today's date by itself. Its value is the combination with
R1: every day is re-examined on each of the next `LookbackDays` nights, so a day skipped
for a transient reason — in progress at the time, or still below 6 h when first seen —
self-corrects on a later run instead of being lost. This is also what makes night shifts
crossing midnight work: a 22:00→06:00 record is open at 03:00 and skipped, then inserted
the following night once closed.

## Configuration

`Logeto:BreakInsertion` gains one key. No existing key changes meaning or value.

| Key | Default | Meaning |
|---|---|---|
| `LookbackDays` | `7` | Days of history scanned before today. Clamped by `StartDate`. |

`backend/src/Anela.Heblo.API/appsettings.json` is updated with the explicit default.

## Testing

New tests in `BreakInsertionServiceTests`:

1. Day with an open work entry (`From` set, `To` null) → no insert, `SkippedInProgress`
   incremented.
2. Today, all records closed, ≥ 6 h → break inserted (proves today is in scope).
3. Past day with an open record → warning logged.
4. `GetTimeTrackingAsync` called with `today - LookbackDays` and `today`.
5. `StartDate` inside the lookback range → window start clamped to `StartDate`.
6. `from > to` → no API call, empty summary.
7. Two-shift 12 h day (e.g. 06:00–12:00 + 13:00–19:00) → exactly one
   `CreateTimeEntryAsync`, placed inside the first segment at the preferred window.

Existing tests are expected to pass unchanged: their fixed "now" of 2026-08-04 with
`StartDate` 2026-08-01 and the default lookback clamps to the same effective range they
already assume.

## Explicit non-goals

- No change to the cron schedule, slot placement, threshold, or merge behaviour.
- No top-up or repositioning of a break once inserted — rule 3.1 of the original design
  still skips any day that already has one, even if work is added to that day later.
- No second break for very long days; the original design's non-goal stands.
