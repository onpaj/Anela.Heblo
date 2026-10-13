---
process: module-attendance
kind: module
module: attendance
summary: Keeps workers' time sheets in Logeto (Výkaz práce) correct — automatic lunch breaks, hours for empty absences — and runs the monthly overtime ledger (Evidence přesčasů) on top of them.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-logeto-break-insertion
  - feed-logeto-absence-hours
  - calc-overtime
  - flow-overtime-month-close
---

# Attendance (Logeto time sheets and overtime)

## Purpose
Anela's workers clock their time in **Logeto** (the phone/web attendance app, time sheet =
"Výkaz práce"). Heblo does two things with that data:

1. **Cleans the time sheets in Logeto itself**, every night:
   - inserts the legally required 30-minute break (přestávka) into any working day of 6 hours
     or more that has none, so the worked time is not overstated by half an hour;
   - writes the worker's daily contracted hours (úvazek) into absence records (vacation,
     sickness, …) that were entered without any time, so such a day does not count as zero.
2. **Keeps the overtime ledger** (Evidence přesčasů): each month it compares the hours a worker
   was credited in Logeto with the hours their contract requires, carries the difference into a
   running balance, lets the office add payouts / purchase deductions / corrections, and on
   month close freezes the numbers and publishes the shared Excel to SharePoint. It replaces the
   hand-maintained Excel that used to be pinned in Teams.

Only workers opted in through their Logeto **Note** take part: the note must start with
`integration`, optionally followed by their net daily hours, e.g. `integration 6,4`
(`IntegrationNote`). That note is the only place Heblo learns a worker's úvazek — the Logeto
API does not expose the "Úvazky pracovníků" screen.

## Users & screens
| Who | Where | What |
|---|---|---|
| Office / payroll (permission `Attendance_Overtime`, menu "Evidence přesčasů") | `/overtime` (`OvertimePage.tsx`) | Pick a month, see each tracked worker's required/worked/absence hours, delta, previous and projected balance and warnings; tick "reviewed"; add/delete adjustments; employee settings (baseline); download the Excel; with Write: upload to SharePoint and close the month (`CloseOvertimeMonthDialog`). |
| Admin (permission `Jobs_Trigger`) | `/recurring-jobs` page and `POST /api/attendance/break-insertion/run` | Enable/disable/trigger the two Logeto jobs; run break insertion over an explicit window (API only, no button in the UI). |

No MCP tools read this module's data.

## Processes
- `feed-logeto-break-insertion` — inserts a 30-min break into ≥6 h days and recreates the work
  records around it in Logeto. Hangfire `logeto-break-insertion`, `0 3 * * *`, off by default;
  also on demand via `POST /api/attendance/break-insertion/run`.
- `feed-logeto-absence-hours` — fills daily contracted hours into time-less absence records in
  Logeto. Hangfire `logeto-absence-hours`, `0 4 * * *`, off by default.
- `calc-overtime` — computes a person-month's required, worked and absence hours and the
  overtime delta live from Logeto. On demand, whenever `/overtime` loads an open month.
- `flow-overtime-month-close` — review → adjustments → close month (freeze, carry the balance)
  → build and upload the "Evidence přesčasů" Excel to SharePoint.

Plain CRUD (no own doc): tracked employees and their baseline (`PUT /api/overtime/employees`,
`UpsertOvertimeEmployeeHandler`; the baseline cannot change once the person has a closed month),
listing tracked and still untracked Logeto people (`GET /api/overtime/employees`).

## Data owned
All in `public` schema of the Heblo DB:
- `OvertimeEmployees` — one row per Logeto person tracked in the ledger: `PersonId` (Logeto
  Guid, unique), `DisplayName`, `BaselineHours` (balance carried over from the legacy Excel),
  `BaselineDate` (Logeto data before it is ignored), `IsActive`.
- `OvertimeMonthlyStatements` — one row per person-month (unique `PersonId`+`Year`+`Month`).
  While `Status = Open` the hour columns are a cache of the last live computation; on close they
  freeze and `BalanceAfter`, `ClosedAtUtc`, `ClosedBy` are written. `IsReviewed` is the
  reconciliation tick.
- `OvertimeAdjustments` — manual signed hour moves (`Payout`, `PurchaseDeduction`, `Correction`,
  `SportBenefit`, `Other`) for a person-month, with note, author and timestamp.

The module writes nothing else in Heblo. Its main "data" lives in Logeto, which it edits.

## External systems
| System | Direction | Calls |
|---|---|---|
| Logeto (`https://{Logeto:AccountName}.logeto.com`, header `AccessKey`) | read + write | `GET /api/v2/Activities`, `GET /api/v2/People`, `GET /api/v2/TimeTracking?From=&To=` (paged by `ContinuationToken`, all people, no person filter); `POST /api/v2/TimeTracking?merge=false`, `PUT /api/v2/TimeTracking/{guid}?merge=false`, `DELETE /api/v2/TimeTracking/{guid}` |
| Microsoft Graph / SharePoint | write | `PUT https://graph.microsoft.com/v1.0/drives/{Overtime:ExportDriveId}/root:/{folder}/{file}:/content?@microsoft.graph.conflictBehavior=replace`, app-only token |

Logeto client: `Anela.Heblo.Adapters.Logeto` (`LogetoClient`), resilience handler `logeto`:
`Logeto:RetryCount` (3) exponential retries with jitter, `Logeto:RequestTimeoutSeconds` (30)
per attempt. `Logeto:AccessKey` comes from Key Vault (`Logeto--AccessKey`).

## Dependencies
- Reads no other Heblo module. Uses the shared recurring-job infrastructure
  (`IRecurringJobStatusChecker`, Recurring Jobs page) and the Graph helpers in
  `Application/Common/Graph`.
- No Heblo module reads its tables. The published Excel is read by people, not code.
- The overtime numbers are only as good as the Logeto data: break insertion removes the
  half-hour that a break-less ≥6 h day would otherwise add to overtime, and absence hours makes
  vacation/sick days count. `calc-overtime` assumes both have run.

## Known quirks
- **Both Logeto jobs are disabled by default** (`DefaultIsEnabled = false`) because they write
  to a live account with no sandbox. An unseeded environment does nothing until someone enables
  them on the Recurring Jobs page.
- **Logeto unconfigured = every call fails.** With `Logeto:AccountName` empty the HttpClient gets
  no base address, so any call (the jobs, and also the `/overtime` page, which reads Logeto live)
  fails with an invalid request URI.
- **The úvazek lives only in the Logeto Note.** A typo (`integration6,4`, a missing number, a
  value outside 0–24) silently drops the worker from both jobs or leaves them without úvazek
  ("Chybí úvazek" on `/overtime`, which blocks month close). The note carries no history: the
  current value applies to every open month.
- **Recreated work records lose GPS.** Break insertion replaces the worker's records with new ones,
  and Logeto's `Location`/`EndLocation` are response-only, so the clock-in/out location is gone
  (spike Finding 6, 2026-09-30).
- **No identity link between Heblo users and Logeto people**, so attendance cannot be joined to
  other Heblo data (noted 2026-09 in the Metabase readiness review).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs` — job services, run gate, options
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs` — ledger services, Graph vs no-op publisher
- `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs` — every Logeto call
- `backend/src/Anela.Heblo.Domain/Features/Attendance/IntegrationNote.cs` — opt-in marker and úvazek parsing
- `backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs`, `AttendanceController.cs` — HTTP surface
- `frontend/src/pages/OvertimePage.tsx` — the `/overtime` screen
- `docs/superpowers/specs/2026-08-05-logeto-spike-results.md` — live Logeto API findings (Findings 1–6)
- `docs/superpowers/specs/2026-08-10-overtime-ledger-design.md` — ledger design
