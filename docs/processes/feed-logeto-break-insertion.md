---
process: feed-logeto-break-insertion
kind: feed
module: attendance
summary: Nightly walk over opted-in workers' Logeto time sheets that inserts a 30-minute break into every working day of 6 h or more without one, and recreates the work records around it so phones show the change.
owns:
  - backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/BreakInsertionJob.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertion*.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/IBreakInsertionRunGate.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakSlotCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/LogetoTimeConverter.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/TimeSlot.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/UseCases/RunBreakInsertion/**
  - backend/src/Anela.Heblo.API/Controllers/AttendanceController.cs
  - backend/src/Anela.Heblo.Domain/Features/Attendance/*.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Logeto/**
verified_at: "5e993f9e2"
related:
  - feed-logeto-absence-hours
  - calc-overtime
---

# Logeto — insert missing lunch breaks

## Purpose
Czech labour law requires a break after 6 hours of work, but workers often clock one continuous
record (e.g. 07:00–15:00) without it. In the time sheet (Výkaz práce) that overstates worked time
by 30 minutes a day, which used to feed straight into overtime. This feed fixes the source: for
every opted-in worker it puts a 30-minute break ("Přestávka", description "Automatická
přestávka") into each such day in Logeto and cuts the work record around it. Workers see the
result in the Logeto app; the office sees it in the overtime ledger (`calc-overtime`).

## Trigger
- Hangfire recurring job **`logeto-break-insertion`**, cron `0 3 * * *` (03:00 Europe/Prague),
  category Attendance, **disabled by default**, `[AutomaticRetry(Attempts = 0)]`. Enable, re-time
  or trigger it on the Recurring Jobs page.
- On demand: `POST /api/attendance/break-insertion/run` with body `{ fromDaysAgo?, toDaysAgo? }`
  (permission `Jobs_Trigger`; no UI button). Refused with `RecurringJobDisabled` when the job is
  disabled and `RecurringJobAlreadyRunning` when a walk is in flight. Runs synchronously and
  returns the summary counters.
- Both paths share one in-process gate (`BreakInsertionRunGate`, singleton): only one walk at a
  time, because two overlapping walks would each insert a break into the same day.

## Data flow
1. Window, Prague dates: `from = max(StartDate, today − fromDaysAgo)`, `to = today − toDaysAgo`.
   Nightly: `fromDaysAgo = LookbackDays` (7), `toDaysAgo = 0`, so 8 days including today.
2. `GET /api/v2/Activities` → find the activity named `BreakActivityName` ("Přestávka", trimmed,
   case-insensitive) of Logeto type `Break`; missing → the whole run fails
   (`ConfigurationError` on the API). Activity type (`Work`/`Break`/`Absence`) decides how each
   record is treated.
3. `GET /api/v2/People` → keep active people whose Note starts with `NoteMarker` ("integration").
4. `GET /api/v2/TimeTracking?From=&To=` → all records in the window (all people), grouped per
   person and day.
5. Per day (errors are isolated per day, counted as `Failed`):
   - any record with a start but no end → skip (`SkippedInProgress`; logged as a warning only
     for past days).
   - the day has a Break record → **heal path** (below), otherwise **insert path**.
6. **Insert path**:
   - sum Work time: clocked records (`To > From`) + duration-only records (`Hours`).
     Total < `MinWorkHours` (6 h, inclusive) → `SkippedBelowThreshold`. Only reaches 6 h with
     duration-only records → `SkippedHoursOnly` (cannot place a break in time).
   - merge the clocked Work records into continuous segments and pick the slot
     (`BreakSlotCalculator`): the preferred window 11:30–12:00 if it lies strictly inside a
     segment; otherwise centred in the longest segment, rounded to 5 min, at least 5 min from
     both edges; no segment ≥ 40 min → `SkippedNoSlot`.
   - the slot overlaps a Work record carrying another integration's `ExternalKey` →
     `SkippedNoSlot` (not ours to rewrite).
   - `POST /api/v2/TimeTracking?merge=false` the break: activity "Přestávka", `Billable=false`,
     `ExternalKey = autobreak-{personGuid}-{yyyy-MM-dd}`.
   - recreate the work around it (below). A failure here counts `RecreateFailed`, not `Failed`:
     the break is in, the next run finishes the day.
7. **Heal path**: only a break carrying this job's `autobreak-{person}-{date}` key counts; a
   break the worker entered themselves → `SkippedExistingBreak`. For our break, recreate the
   work around it; if anything was recreated → `DaysHealed`, else `SkippedExistingBreak`.
8. **Recreate work around the break**: take every Work record that is the worker's own (no key)
   and touches or overlaps the break, or is one of our replacements (`autobreak-{person}-{date}-…`)
   that overlaps it. For each, POST (`merge=false`) the parts before and after the break as new
   records with `ExternalKey = autobreak-{person}-{date}-{HHmm of piece start}-{first 8 hex of
   the original Guid}`, copying activity, description, billable, contract and subcontract.
   Pieces whose key already exists are skipped. **Only after all creates**, `DELETE` each
   original (`RecordsRecreated` per delete). A record fully inside the break is deleted with no
   replacement.
9. One summary log line: days scanned, breaks inserted, days healed, records recreated, skips per
   reason, failures.

Target: Logeto `TimeTracking` records only. Heblo stores nothing.

## Logic & formulas
- Times: the Logeto API takes and returns Prague wall-clock time with no offset
  (`ApiTimesAreUtc = false`); seconds are always sent as `:00`.
- Break: `BreakDurationMinutes` (30) from `PreferredWindowStart` (11:30) when possible.
- Threshold uses Work-type records only; absences and breaks never count toward the 6 h.
- Why recreate instead of letting Logeto split (`merge=true`): an API rewrite of an existing
  record never moves `TimestampChanged`, so the phone app kept the old full-day record next to
  the break ("křížení času"). New records have fresh Guid, Revision and timestamps.
- Idempotent: our break and our pieces are keyed, so a rerun skips what exists; creates before
  deletes means a crash can leave a day overlapping, never short of worked time.
- On-demand window rules (`RunBreakInsertionValidator`): both values ≥ 0, `fromDaysAgo ≥
  toDaysAgo`, and `fromDaysAgo − toDaysAgo < 14` (`MaxWindowDays`). Without `fromDaysAgo` the
  configured lookback is used. The window is still clamped to `StartDate`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Logeto:AccountName` | "" | Account subdomain (`anelacosmetics` per the spike); empty = adapter unconfigured |
| `Logeto:AccessKey` | "" (Key Vault `Logeto--AccessKey`) | API key, header `AccessKey` |
| `Logeto:RetryCount` | 3 (class default) | Polly retries per call, exponential from 1 s, with jitter |
| `Logeto:RequestTimeoutSeconds` | 30 (class default) | Per-attempt timeout |
| `Logeto:BreakInsertion:StartDate` | 2026-08-01 | No day before this is ever touched |
| `Logeto:BreakInsertion:LookbackDays` | 7 | Nightly window = today − 7 … today |
| `Logeto:BreakInsertion:NoteMarker` | integration | Opt-in prefix of the person's Note |
| `Logeto:BreakInsertion:BreakActivityName` | Přestávka | Break activity to insert (the account also has "Oběd") |
| `Logeto:BreakInsertion:PreferredWindowStart` | 11:30 | Preferred break start |
| `Logeto:BreakInsertion:BreakDurationMinutes` | 30 | Break length |
| `Logeto:BreakInsertion:MinWorkHours` | 6 | Inclusive threshold that requires a break |
| `Logeto:BreakInsertion:ApiTimesAreUtc` | false | Logeto API times are Prague local time (spike Finding 3) |

Options are read once at startup (`IOptions`), so changing an App Setting needs a real restart.

## Runtime facts
- The job ran nightly in production in September 2026: every auto-break day from 21.9. was
  processed inline by the 03:00 run — agent memory `gotcha_logeto_merge_no_revision_bump` — 2026-09-30.
- `Logeto__BreakInsertion__LookbackDays` is overridden as an App Setting on the production web
  app `heblo` (value not recorded) — agent memory `ops_azure_webapp_vault_mapping` — 2026-09.
- Historical clean-ups were one-shot scripts, not this job: 292 work records re-touched for
  2026-08-01…09-18 (2026-09-18), 106 records for 21.–29.9 (2026-09-30), and 5 reported days
  recreated by hand (2026-10-01) — agent memory `gotcha_logeto_merge_no_revision_bump`.

## Known quirks
- **Do not sweep history with the run endpoint to "heal" days.** It has no heal-only mode: it
  also inserts breaks into any ≥ 6 h day without one, which retroactively cuts worked hours in
  months whose overtime may already be closed (closed statements stay frozen, so Logeto and the
  ledger then disagree). Agent memory, 2026-09-30.
- **Recreated records lose GPS** (`Location`/`EndLocation` are response-only), and the new record
  gets a new Guid. Whether the phone app drops a record deleted through the API was still
  unverified on 2026-10-01 (spike Finding 6).
- **History of the fix:** `merge=true` split (until 2026-09-16) → split + no-op PUT to bump
  `Revision` (2026-09-16) → still reported overlaps on phones → recreate (PR #4361, 2026-09-30).
  Days split by older versions are finished by the heal path when they fall into a window.
- **A worker's own break is never touched**, even if it overlaps their work record, so such a
  day is not corrected and its overlap still counts as worked time in `calc-overtime`.
- **Retries of a non-idempotent POST.** The `logeto` resilience handler retries POST and DELETE
  too. A create that landed but timed out is re-sent; because every record this job creates is
  keyed and Logeto enforces unique `ExternalKey`, the retry should fail with
  `ExternalKeyUniqueViolation` and the day is finished next run (read from code + spike
  Finding 2, not observed).
- **Records with non-zero seconds** would be shifted by up to 59 s when recreated (times are
  always sent as `:00`). None seen so far (spike Finding 5).
- **The gate is per process.** Fine for the single-container deployment; a second instance would
  not see it.
- **The job body fails open on a missing settings row.** `ExecuteAsync` calls
  `IsJobEnabledAsync` without `defaultIfMissing`, which defaults to `true`, so if the job's
  configuration row were missing the job would run; the seeder normally creates the row as
  disabled at startup. The on-demand endpoint passes `DefaultEnabled` (false) and fails closed.
- `BreakInsertionOptions.NoteMarker` says the Note must *equal* the marker; the code accepts a
  Note that *starts with* it (`integration 6,4`), as the absence-hours job does.
- **Disabled job + Recurring Jobs "trigger"**: the job body itself checks the enabled flag and
  returns, so a manual trigger of a disabled job does nothing.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs` — the walk, both paths, keys
- `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakSlotCalculator.cs` — slot choice
- `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/BreakInsertionJob.cs` — job id, cron, default off
- `backend/src/Anela.Heblo.Application/Features/Attendance/UseCases/RunBreakInsertion/RunBreakInsertionHandler.cs` — on-demand run, error mapping
- `backend/src/Anela.Heblo.Application/Features/Attendance/UseCases/RunBreakInsertion/RunBreakInsertionValidator.cs` — window limits
- `backend/src/Anela.Heblo.API/Controllers/AttendanceController.cs` — `POST /api/attendance/break-insertion/run`
- `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs` — Logeto calls, paging, error envelope
- `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoAdapterModule.cs` — HttpClient, retries, timeout
- `docs/superpowers/specs/2026-08-05-logeto-spike-results.md` — live API behaviour (Findings 1–6)
