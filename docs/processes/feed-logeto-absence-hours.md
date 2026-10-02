---
process: feed-logeto-absence-hours
kind: feed
module: attendance
summary: Nightly job that writes each opted-in worker's net daily contracted hours into Logeto absence records (vacation, sickness, …) entered with no time and no hours, so such days count in the time sheet and the overtime ledger.
owns:
  - backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/AbsenceHoursOptions.cs
  - backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs
verified_at: "5e993f9e2"
related:
  - feed-logeto-break-insertion
  - calc-overtime
---

# Logeto — fill hours into time-less absences

## Purpose
Absences in Logeto (Dovolená, Nemoc, Lékař, OČR, …) are routinely entered as a whole-day record
with no From/To and no Hours. In the time sheet (Výkaz práce) such a day is worth zero hours, so
a week of vacation would look like a week of missing work in the overtime ledger. This feed fills
the worker's net daily contracted hours (úvazek, without the break, e.g. 6:24 for 6,4 h) into
those records in Logeto.

## Trigger
Hangfire recurring job **`logeto-absence-hours`**, cron `0 4 * * *` (04:00 Europe/Prague, one hour
after break insertion), category Attendance, **disabled by default**,
`[AutomaticRetry(Attempts = 0)]`. Enable or trigger it on the Recurring Jobs page. No dedicated
API endpoint.

## Data flow
1. Window, Prague dates: `from = max(StartDate, today − LookbackDays)`, `to = today − 1`.
   **Past days only** — today is skipped because the worker may still be editing it.
2. `GET /api/v2/Activities` → the set of activities whose Logeto type is `Absence`.
3. `GET /api/v2/People` → active people whose Note starts with `NoteMarker` ("integration");
   their daily hours are parsed from the rest of the Note (`IntegrationNote`).
4. `GET /api/v2/TimeTracking?From=&To=` → all records in the window, grouped per person and day.
5. Per day, "empty absence" = Absence-type record with no From, no To and blank Hours:
   - none → nothing to do (normal working day, not counted);
   - the day also has any other record → skip (`SkippedMixedDay`): it may be a half-day absence;
   - more than one empty absence → skip (`SkippedAmbiguous`);
   - the Note has no usable hours → skip (`SkippedNoHours`, log says to set e.g.
     `integration 6,4` in Pracovníci → Note);
   - otherwise `PUT /api/v2/TimeTracking/{guid}?merge=false` with every field resent unchanged
     (person, activity, date, billable, description, external key, contract, subcontract) and
     `Hours = "HH:mm:00"`.
6. Errors are isolated per day (`Failed`); one summary log line at the end.

Target: the `Hours` field of the Logeto record. Heblo stores nothing.

## Logic & formulas
- Daily hours from the Note: text after the marker, decimal comma or dot, must be > 0 and ≤ 24,
  rounded to whole minutes (`6,4` → 6 h 24 min → `"06:24:00"`).
- Note matching: trimmed, case-insensitive prefix; `integrationX` (no whitespace after the
  marker) is not enrolled.
- Idempotent without a marker: once Hours is set the record no longer matches "empty". No
  `ExternalKey` is stamped (a keyed record breaks a later Logeto `merge=true` split — spike
  Finding 2).
- The PUT is a full replacement, which is why every writable field is resent.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Logeto:AbsenceHours:StartDate` | 2026-08-01 | No day before this is ever touched |
| `Logeto:AbsenceHours:LookbackDays` | 7 | Window = today − 7 … yesterday |
| `Logeto:AbsenceHours:NoteMarker` | integration | Opt-in prefix of the person's Note |
| `Logeto:AccountName`, `Logeto:AccessKey`, `Logeto:RetryCount`, `Logeto:RequestTimeoutSeconds` | see `feed-logeto-break-insertion` | Shared Logeto client |

## Runtime facts
None. Whether the job is enabled in production is not recorded.

## Known quirks
- **A record older than the window is never filled.** Missed days (job disabled, Note without
  hours at the time) stay at zero; widening `LookbackDays` temporarily is the way to backfill.
- **Uses the current úvazek for past days.** The Note has no history, so a changed úvazek is
  written into absences of the previous week too.
- **Half-day absences are left alone** by design (mixed day); the office fills them by hand.
- **The job body fails open on a missing settings row.** `ExecuteAsync` calls
  `IsJobEnabledAsync` without `defaultIfMissing`, which defaults to `true`, so if the job's
  configuration row were missing the job would run; the seeder normally creates the row as
  disabled at startup.
- **Every Absence-type activity is filled**, including "Náhradní volno" if it is an Absence in
  Logeto; `calc-overtime` shows comp time but never credits it, so this is harmless for the
  ledger.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Attendance/Services/AbsenceHoursService.cs` — window, day rules, PUT body
- `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/AbsenceHoursJob.cs` — job id, cron, default off
- `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs` — Note parsing
- `docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md` — design
