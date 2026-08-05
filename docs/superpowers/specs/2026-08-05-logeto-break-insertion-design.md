# Logeto Automatic Break Insertion — Design

**Date**: 2026-08-05
**Status**: Approved

## Problem

Workers tracked in Výkaz práce (Logeto) sometimes forget to record their lunch break.
A break record only reduces worked time when it *interrupts* the work period — a break
merely added on top of an existing work record **adds** time instead of subtracting it.
Czech labor practice requires a 30-minute break within any working day of 6+ hours.

We want a recurring job that finds such days and inserts the missing break correctly,
splitting the work record around it.

## Background: Logeto API facts (verified from official docs)

- REST API v2, base URL `https://[AccountName].logeto.com`, auth via `AccessKey` header.
- `POST /api/v2/TimeTracking` and `PUT /api/v2/TimeTracking/:key` accept a query
  parameter **`merge` (boolean) — "Merge overlapping records"**. This is expected to
  replicate the UI's split-on-conflict behavior (Práce 8:00–16:45 + Oběd 12:00–12:30 →
  Práce 8:00–12:00, Oběd 12:00–12:30, Práce 12:30–16:45). **Not yet verified against a
  live account — see Verification spike.**
- TimeTracking body: `Person` (guid, required), `Activity` (guid, required), `Date`
  (required), `Billable` (required), `From`/`To` (date-time, seconds must be `:00`) **or**
  `Hours` (duration) — records entered with `Hours` only have no clock window.
- Activities are account-configured records with a type of Work / Absence / Break /
  Auxiliary; the break activity must be referenced by its GUID.
- Docs: https://documentation.logeto.com/ (English), https://dokumentace.vykazprace.cz/
  (Czech). Integration contact: krystof.macek@vykazprace.cz.

## Requirements

1. **Worker selection**: `GET /api/v2/People`; process workers whose **Note** field
   equals `integration` (trimmed, case-insensitive). Currently one worker matches.
2. **Daily walk**: for each selected worker, iterate days from configured `StartDate`
   up to and including **yesterday** (never today).
3. **Per-day rules**, in order:
   1. If any Break-type record exists that day (any length) → skip the day.
   2. Sum Work-type record durations; if total < 6 h → skip the day.
   3. If the ≥6 h total depends on records without a `From`/`To` window (`Hours`-only)
      → log a warning and skip the day (human intervention required).
   4. Otherwise insert one **30-minute break** that **interrupts the work**:
      - Preferred slot **11:00–11:30 Europe/Prague**, if fully inside a single
        continuous work segment.
      - Fallback: **center of the longest continuous work segment**, rounded to the
        nearest 5 minutes.
      - The break is never placed at the start or end of the shift.
   5. Insert via `POST /TimeTracking?merge=true` with
      `ExternalKey = autobreak-{personGuid}-{yyyy-MM-dd}`,
      `Description = "Automatická přestávka"`, `Billable = false`.
4. **Invariant**: after a successful run, every processed day with ≥ 6 h of
   window-based work contains at least one break during the working time.
5. **Idempotency**: rule 3.1 guarantees it — the previously inserted break causes the
   day to be skipped on subsequent runs. `ExternalKey` additionally identifies
   auto-inserted records.

### Explicit non-goals (YAGNI)

- No "no continuous segment > 6 h" enforcement: an extremely long day (> 12.5 h) with a
  single centered break may still contain a > 6 h segment. Acceptable for the current
  single worker on ~8 h shifts.
- No top-up of short existing breaks (a 10-minute recorded break counts as handled).
- No UI in Anela Heblo.

## Architecture

### New adapter: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto`

Mirrors the existing adapter pattern (Shoptet et al.):

- `LogetoOptions` — `AccountName`, `AccessKey`. AccessKey lives in Key Vault as
  `Logeto--AccessKey` (staging `kv-heblo-stg`, production `kv-heblo-prod`); never in
  App Settings.
- `LogetoClient` — typed `HttpClient`, base address `https://{AccountName}.logeto.com`,
  `AccessKey` header, Polly retry pipeline **wrapping** status handling (avoid the
  EnsureSuccessStatusCode-outside-pipeline bug fixed in #3843).
- Methods (only what is needed):
  - `GetPeopleAsync()`
  - `GetActivitiesAsync()`
  - `GetTimeTrackingAsync(personId, dateFrom, dateTo)`
  - `CreateTimeTrackingAsync(record, merge: true)`
- DTOs are **classes**, not records (project rule for external contracts).

### Application slice: `Attendance` in `Anela.Heblo.Application`

- `BreakInsertionService` — the day-walk orchestration. The placement decision is a
  **pure function** (`ComputeBreakSlot(workSegments, preferredWindow, breakDuration)`)
  with no I/O, unit-testable in isolation.
- `BreakInsertionJob` — thin Hangfire recurring job, nightly at 03:00 Europe/Prague.
  Resolves config, runs the service, logs a run summary (days scanned, breaks inserted,
  skips by reason, failures).
- Per-run validation: resolve the break activity by configured name from
  `GET /Activities`, verify its type is Break; abort the run with a logged error if
  missing or wrong type.

### Configuration (`Logeto:BreakInsertion`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master switch; stays off in production until the spike passes. |
| `StartDate` | — (required) | First day of the daily walk. |
| `NoteMarker` | `integration` | People Note value that opts a worker in. |
| `BreakActivityName` | — (required) | Name of the Break-type activity to insert. |
| `PreferredWindowStart` | `11:00` | Local (Europe/Prague) preferred break start. |
| `BreakDuration` | `00:30` | Inserted break length. |
| `MinWorkHours` | `6` | Daily worked-hours threshold that triggers insertion. |

### Time handling

Whether the API's `From`/`To` are UTC or account-local is **unknown** (docs show
`Z`-suffixed examples; Czech products often store local time). The verification spike
answers this; the service converts the Prague-local preferred window into whatever
representation `GET /TimeTracking` demonstrably uses.

## Error handling

- **Per-day isolation**: any 4xx/5xx on a single day's insert is logged (worker, date,
  response body) and the walk continues; failures appear in the run summary and are
  retried naturally on the next nightly run.
- **Fatal**: People or Activities fetch failure aborts the run with a logged error.
- **Safety**: in the `merge=true` path the job only ever creates records — worst case
  is a missing break, never corrupted/lost work time.

## Testing

- **Unit** (TDD, the placement function is the heart):
  - preferred window fits inside a segment;
  - work starts 11:15 → fallback;
  - afternoon shift → fallback;
  - multiple segments → longest wins;
  - rounding to 5 minutes;
  - < 6 h total → skip;
  - existing break → skip;
  - `Hours`-only records → skip with warning;
  - exactly 6 h boundary (inclusive: 6 h triggers insertion).
- **Integration**: `LogetoClient` against a mocked HTTP handler — auth header, URL and
  `merge=true` query shape, error propagation.
- **No E2E**: no UI surface.

## Rollout — verification spike first

Before any job code is written:

1. With the real AccessKey: `GET /Activities` and `GET /TimeTracking` for the
   integration worker — verifies auth, the time representation (UTC vs local), and the
   break activity GUID.
2. `POST` one break with `merge=true` onto a real ≥ 6 h day; confirm in the Logeto UI
   that the work record split into two parts around the break.
3. **If merge does not split**: extend this design with a manual split path
   (PUT to shorten the work record + POST the second work part + POST the break) and
   record the finding here before implementation continues.

Ship with `Enabled=false` in production until the spike passes.

## As-built deltas

This section records where the shipped implementation diverged from the design above,
captured during the final whole-branch code review.

1. **No `Enabled` config key exists.** The `Logeto:BreakInsertion` config section
   (`BreakInsertionOptions`) has no `Enabled` flag. The real on/off switch is
   `BreakInsertionJob.Metadata.DefaultIsEnabled` (defaults to `false`) combined with the
   existing BackgroundJobs admin panel/DB row — the same mechanism every other recurring
   job in this codebase uses. This reuses existing infrastructure instead of adding a
   parallel Logeto-specific flag; a good decision, just never backported to this spec.
2. **`BreakDuration` (TimeSpan-shaped, `00:30`) became `BreakDurationMinutes` (`int`, `30`)**
   in the shipped `BreakInsertionOptions`.
3. **`ILogetoClient.GetTimeTrackingAsync` has no `personId` parameter** — its shipped
   signature is `GetTimeTrackingAsync(DateOnly from, DateOnly to, CancellationToken)`.
   The real Logeto `TimeTracking` endpoint has no server-side person filter; the client
   fetches the full date range for all people and `BreakInsertionService` filters
   client-side per person.
