# Logeto — fill hours into timeless absence records

**Date**: 2026-08-10
**Account**: `anelacosmetics` (https://anelacosmetics.logeto.com)
**Related**: `docs/superpowers/specs/2026-08-05-logeto-break-insertion-design.md`,
`docs/superpowers/specs/2026-08-05-logeto-spike-results.md`

## Problem

Absence records in Logeto (Dovolená, Nemoc, Sick day, Náhradní volno, Lékař) are
frequently entered with **no time information at all** — no `From`/`To` window and
no `Hours` duration. Such a day contributes zero hours to the timesheet, so a
worker's vacation day reads as an empty day.

Measured over 2026-05-01 → 2026-08-10 across the whole account:

| Activity | Type | Empty (no From/To, no Hours) | Hours-only | Window |
|---|---|---|---|---|
| Dovolená | Absence | 114 | 3 | 0 |
| Nemoc | Absence | 30 | 0 | 0 |
| Náhradní volno | Absence | 1 | 2 | 0 |
| Sick day | Absence | 0 | 1 | 0 |
| Lékař | Absence | 0 | 3 | 24 |

For the four opted-in people (Note = `integration`) there are 32 empty records in
that period, all `Dovolená`.

## Solution

A new nightly Hangfire job walks a rolling window of **past** days for each
opted-in person and writes the person's net daily contracted hours into every
absence record that has no time. Idempotent: once `Hours` is set the record no
longer matches.

Break insertion is unchanged and continues to cover today as well. Only this new
job is past-only, because a same-day absence may still be edited by the worker.

## Where the daily hours come from

### The API does not expose úvazek

The Logeto UI has `Pracovníci → Úvazky pracovníků`, a per-person assignment with a
`Platnost od` validity date pointing at a `Pracovní doba` template — e.g.
`80% úvazek bez pauz (6,4hod/den)`, `100% úvazek (8,5hod/den)`,
`87,5% úvazek (7,5hod/den)`. This is exactly the data we need and it is **not
reachable through the public API**. Verified three ways on 2026-08-10:

1. **The account's own OpenAPI spec** (`GET /swagger/v2/swagger.json`, "Logeto
   public API 2.0.0", 30 paths). Full-text search of the document finds zero
   occurrences of `Workload`, `WorkTime`, `Working`, `Schedule`, `Shift`,
   `Capacity`, `Fund`, `FTE`. `PeopleResponseItem`, `PeopleRequestPost` and
   `PeopleRequestPatch` expose only names, `Code`, `Email`, `PhoneNumber`,
   `Note`, `Inactive`, `ClosedPeriodTo`, `Branch`, `ExternalKey`, `AccessLevel`,
   `LegislationCountry`, `Language` and seven admin booleans.
2. **The public documentation** (https://documentation.logeto.com/) lists the same
   18 resources as the account swagger: Activities, Branches, Common, Contracts,
   ContractsStates, Customers, CustomFields, CustomFieldsList, EventLog, Events,
   Groups, Holidays, MonitoredLocations, People, Plan, Subcontracts, TimeTracking.
   No Úvazky, no Pracovní doby. The Czech docs
   (https://dokumentace.vykazprace.cz/aplikace-na-webu/API/) only redirect there.
3. **Brute-force probing** of 30+ plausible names (`Workloads`, `WorkTimes`,
   `WorkingHours`, `Employments`, `Jobs`, `Assignments`, `Schedules`,
   `People/Contracts`, `PersonWorkTime`, …) — all 404. `/api/v1` is blocked at
   CloudFront, `/api/v3` does not exist.

Dead ends ruled out along the way:

- `Contracts` is customer/business contracts, not employment contracts.
- `Plan` has the right shape (`Person`, `Date`, `Hours`) but returns **zero
  records** — nobody plans shifts in this account.
- `Groups` (`HPP full`, `HPP 0,8`, `HPP 0,55`, `DPP`, `Archiv`) encodes úvazek
  *percentages*, but membership is stale: Olga Petrová, added to Logeto in
  2025-08, belongs to no úvazek group. Confirmed unpaged via both
  `GET /Groups/Members` (48 rows, `ContinuationToken: null`) and
  `GET /Groups/Membership/{guid}`. `HPP full` also carries no number, so parsing
  these names would still need a base-hours constant on our side.
- Person custom fields: the `EntityType` enum does include `Person`, but
  `PeopleResponseItem` has no `CustomFields` property (unlike
  `TimeTrackingResponseItem` and `PlanResponseItem`, which both do), so a Person
  custom field would not come back through the API.
- `EventLog` is the clock-in/out event stream, not a settings audit trail.

### Decision: the `Note` field carries the hours

`LogetoPerson.Note` becomes `integration 6,4` — one field that both enrolls the
person in the integration and states their **net** daily hours. This keeps a
single source of truth inside Logeto, editable in the person's UI form, and adds
no new Logeto setup.

Net, not gross: a vacation day is not interrupted by an unpaid break, so a person
on `80% úvazek bez pauz (6,4hod/den)` gets `6,4` — not the `6,9` that the
break-inclusive template variant would state.

Grammar, implemented by a new `IntegrationNote` domain type:

| `Note` | Enrolled | Daily hours |
|---|---|---|
| `integration` | yes | none |
| `integration 6,4` | yes | 6h 24m |
| `integration 6.4` | yes | 6h 24m |
| `integration 8` | yes | 8h |
| `integration 25` | yes | none (out of range 0–24, warned) |
| `integration abc` | yes | none (unparseable, warned) |
| `null`, `""`, anything else | no | — |

Matching on the marker is trimmed and case-insensitive. Number parsing is
culture-invariant, accepting both `,` and `.` so behaviour does not depend on the
server locale.

### Consequence: break insertion must change

`BreakInsertionService` currently selects people with
`string.Equals(p.Note?.Trim(), options.NoteMarker, StringComparison.OrdinalIgnoreCase)`
— *exact* equality. The moment a note becomes `integration 6,4` that person stops
matching and their breaks silently stop being inserted.

Both services therefore move to the shared `IntegrationNote` parser. This is a
required part of the change, not an optional cleanup, and it needs a regression
test.

## Components

| File | Change |
|---|---|
| `Domain/Features/Attendance/IntegrationNote.cs` | **new** — parses `Note` → `(IsEnrolled, TimeSpan? DailyHours)` |
| `Domain/Features/Attendance/ILogetoClient.cs` | `+ UpdateTimeEntryAsync(Guid guid, LogetoTimeEntryRequest request, CancellationToken)` |
| `Domain/Features/Attendance/LogetoTimeEntry.cs` | `+ Billable`, `+ Contract`, `+ Subcontract` — required to round-trip a PUT |
| `Domain/Features/Attendance/LogetoCreateTimeEntryRequest.cs` | **renamed** to `LogetoTimeEntryRequest.cs`; `+ Hours`, `+ Contract`, `+ Subcontract`. Logeto uses one `TimeTrackingRequest` body for both POST and PUT |
| `Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs` | `PUT /api/v2/TimeTracking/{guid}?merge=false` |
| `Application/Features/Attendance/AbsenceHoursOptions.cs` | **new** — `StartDate`, `LookbackDays`, `NoteMarker` |
| `Application/Features/Attendance/Services/AbsenceHoursService.cs` | **new** — the walk and the decision rules |
| `Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs` | **new** — recurring job, `DefaultIsEnabled = false` |
| `Application/Features/Attendance/Services/BreakInsertionService.cs` | person selection switches to `IntegrationNote` |
| `Application/Features/Attendance/AttendanceModule.cs` | register options, service, job |

A separate job rather than an extension of break insertion: independent cron,
independent enable/disable, no shared state. The two cannot interfere —
`BreakInsertionService` counts only `Work`-type entries, so a filled 6,4h
`Dovolená` still totals zero work hours and stays below the break threshold.

## Data flow

1. Compute the window in Prague time: `from = max(StartDate, today - LookbackDays)`,
   `to = today - 1 day`. If `from > to`, log and return.
2. `GetActivitiesAsync()` → set of `Absence`-type activity guids.
3. `GetPeopleAsync()` → people where `!Inactive` and `IntegrationNote.IsEnrolled`,
   each carrying its parsed `DailyHours`.
4. `GetTimeTrackingAsync(from, to)` → all records in the window (the API has no
   person filter).
5. Per person, per day in the window: apply the guards below, then `PUT` the
   record with `Hours` set.
6. Log a summary.

## Safety rules — never guess

Days outside the window, and days at or after today, are never entered. For each
remaining day the guards are evaluated **in this order**, so each skip reason is
reachable and reports the real cause:

1. Collect the day's empty absence records — `Absence` type, `From == null`,
   `To == null`, `Hours` null or blank. **None → nothing to do**, no counter, no
   log. This is the overwhelmingly common case (a normal working day).
2. The day holds any record that is *not* one of those → **`SkippedMixedDay`**.
   Catches an absence sharing a day with work, a break, or an already-timed
   absence.
3. More than one empty absence record on the day → **`SkippedAmbiguous`**. The
   day's hours cannot be split between them without guessing.
4. The person's note yields no hours → **`SkippedNoHours`**, warning naming the
   person.
5. Otherwise fill.

The mixed-day guard matters: a half-day absence alongside half a day of work would
otherwise receive a full day's hours. All 32 empty records for opted-in people in
the sampled period are sole-record days, so these guards are expected to fire
rarely — they exist so a future half-day is not silently double-counted.

`SkippedNoHours` is the expected state until the notes are updated in Logeto, so
its warning must name the person clearly enough to act on.

## The PUT

`PUT /api/v2/TimeTracking/{guid}?merge=false`. The body is a full replacement, so
every preserved field is resent unchanged and only `Hours` is set:

```
Person, Activity, Date, Description, ExternalKey, Billable, Contract, Subcontract
```

`Hours` is formatted `HH:mm:00` — the API requires seconds and requires them to be
zero. `6,4` → `06:24:00`.

No `ExternalKey` of our own is stamped. Per Finding 2 of the 2026-08-05 spike, a
record carrying an `ExternalKey` throws `ExternalKeyUniqueViolation` if it is
later split by a `merge=true` insert. Idempotency does not need a marker: a record
with `Hours` set no longer matches the empty filter.

The account has zero custom fields defined
(`GET /TimeTracking/CustomFields` → 0 items), so there are none to preserve.

## Error handling

Per-record `try/catch` inside the day loop, mirroring `ProcessDayAsync` in
`BreakInsertionService`: one failing record increments `Failed`, logs with the
person guid and date, and the run continues. The job itself uses
`[AutomaticRetry(Attempts = 0)]` like `BreakInsertionJob` — a failed run is
retried by the next night's schedule, and the work is idempotent.

Summary counters: `RecordsScanned`, `HoursFilled`, `SkippedNoHours`,
`SkippedAmbiguous`, `SkippedMixedDay`, `Failed`.

## Testing

`AbsenceHoursServiceTests` with a fake `ILogetoClient`, mirroring
`BreakInsertionServiceTests`:

- fills a lone empty absence day
- ignores today
- ignores a record that already has `Hours`
- ignores a record that has `From`/`To`
- ignores non-`Absence` activity types
- skips a mixed day (absence + work) and counts `SkippedMixedDay`
- skips two empty absences on one day and counts `SkippedAmbiguous`
- skips a person whose note carries no hours and counts `SkippedNoHours`
- preserves `Description`, `ExternalKey`, `Billable`, `Contract`, `Subcontract`
  on the PUT
- formats `6,4` as `06:24:00`
- a failing PUT increments `Failed` and does not abort the run

`IntegrationNoteTests` covers every row of the grammar table, including the
culture-invariant decimal separator and the out-of-range rejection.

`LogetoClientTests` pins the PUT URL and serialized body.

`AbsenceHoursJobTests` mirrors `BreakInsertionJobTests` (disabled job does
nothing; enabled job calls the service).

`BreakInsertionServiceTests` gains a regression test: a person with note
`integration 6,4` is still selected.

## Configuration

```json
"Logeto": {
  "AbsenceHours": {
    "StartDate": "2026-08-01",
    "LookbackDays": 7,
    "NoteMarker": "integration"
  }
}
```

Job metadata: name `logeto-absence-hours`, cron `0 4 * * *` — an hour after break
insertion — and `DefaultIsEnabled = false`, matching `BreakInsertionJob`.

## Operational prerequisite

The job fills nothing until the notes are set in Logeto. Required updates to
`Pracovníci → Note`, per the Úvazky screen:

| Person | Current note | New note |
|---|---|---|
| Andrea Pajgrt | `integration` | `integration 8` (HPP full) |
| Petra Zilvarová | `integration` | `integration 6,4` (80% bez pauz) |
| Olga Petrová | `integration` | `integration 6,4` (80% bez pauz) |
| Lydie Fellnerová | `integration` | to confirm against the Úvazky screen; `HPP 0,8` suggests `integration 6,4` |

Until a person's note is updated, their absence days are skipped with a
`SkippedNoHours` warning and their break insertion is unaffected.

## Out of scope

- Half-day absences. Mixed days are skipped and warned, not apportioned.
- Backfilling history beyond `LookbackDays`. Widening the window is a config
  change, not a code change — but note the gap this creates: a timeless
  absence that ages past `LookbackDays` before ever being filled (job left
  disabled, failing, or Logeto unreachable for longer than `LookbackDays`)
  drops out of the window permanently and is never revisited. The summary
  counters only report on records inside the current window, so there is no
  signal distinguishing "nothing to do" from "records aged out unfilled."
  This is a real risk during rollout if enabling the job slips past
  `LookbackDays` (7 by default) after merge.
- Reading úvazek from Logeto automatically. If Systemart ever exposes
  `Úvazky pracovníků` in the public API, only `IntegrationNote`'s callers need to
  change — the rest of the service is unaffected.
