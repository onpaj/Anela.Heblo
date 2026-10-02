---
process: calc-overtime
kind: calculation
module: attendance
summary: Computes each tracked worker's monthly required hours, credited worked/absence hours and overtime delta live from Logeto, and the running balance shown on /overtime.
owns:
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/OvertimeCalculationService.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/CzechHolidays.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/WorkingDaysCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/GetMonthlyStatements/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Contracts/OvertimeStatementDto.cs
  - backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/IContractHoursProvider.cs
verified_at: "5e993f9e2"
related:
  - feed-logeto-break-insertion
  - feed-logeto-absence-hours
  - flow-overtime-month-close
---

# Overtime calculation (Evidence přesčasů)

## Purpose
Answers "how many hours of overtime (přesčas) did each worker build up this month, and what is
their balance now?". Overtime is Anela's informal currency: it accumulates month to month, is
taken as comp time (náhradní volno), paid out, or reduced by staff purchases. The office sees
the result on `/overtime` ("Evidence přesčasů") per month and person; the frozen version goes into
the published Excel (`flow-overtime-month-close`).

## Trigger
On demand, no job: `GET /api/overtime/statements/{year}/{month}` every time `/overtime` shows a
month (and on its refresh button). A closed month is read from the DB and not recomputed. Month
close runs the same computation once more (`flow-overtime-month-close`).

## Data flow
1. Tracked people: `public."OvertimeEmployees"` with `IsActive = true`.
2. Logeto: `GET /api/v2/Activities` and `GET /api/v2/TimeTracking?From={1st}&To={last day of month}`
   (all records of all people in the month).
3. Daily úvazek per person: `GET /api/v2/People`, Note `integration <hours>` (one call per
   request, memoised by `LogetoContractHoursProvider`).
4. Compute per person (below).
5. Cache: for an open month each person's numbers are written to
   `public."OvertimeMonthlyStatements"` (row created on first view, `Status = Open`) — so a GET
   writes to the DB.
6. Balance for display: previous balance = `BalanceAfter` of the person's latest closed
   statement, else `BaselineHours`; adjustments = sum of the month's `OvertimeAdjustments`;
   **projected balance = previous + delta + adjustments**.

## Logic & formulas
All figures are hours, rounded to 2 decimals (half away from zero).

- **Effective start** = later of the 1st of the month and the person's `BaselineDate`; a person
  whose baseline is after the month is skipped. Records before the effective start are ignored.
- **Activity categories**: a Logeto activity whose name is in `Overtime:ActivityCategories` gets
  that category; otherwise Logeto type `Break` → Break, `Work` → Work, anything else → Other.
- **Record hours**: `To − From` when both are set; a record with a start but no end counts 0
  (warning "Neuzavřený záznam"); otherwise the `Hours` field; nothing → 0 (warning "Záznam bez
  hodin").

| Category | Column | Credited toward required hours |
|---|---|---|
| Work | Odpracováno (`WorkedHours`) | yes |
| Vacation (Dovolená) | `VacationHours` | yes |
| Sick (Nemoc, Sick day) | `SickHours` | yes |
| Doctor (Lékař) | `DoctorHours` | yes |
| Ocr (OČR) | `OtherAbsenceHours` | yes |
| CompTime (Náhradní volno) | `CompTimeHours` | **no** — taking comp time spends overtime |
| Other (unmapped non-work) | `OtherAbsenceHours` | **no**, warning "Nezařazená aktivita" |
| Break | — | ignored |

- **Required hours** = number of working days from the effective start to the end of the month
  × daily úvazek. Working day = Monday–Friday that is not a Czech public holiday (fixed dates
  1.1., 1.5., 8.5., 5.7., 6.7., 28.9., 28.10., 17.11., 24.–26.12., plus Good Friday and Easter
  Monday computed from Easter). No úvazek → required 0 and warning "Chybí úvazek".
- **Delta** = round(sum of credited hours) − required hours.
- Weekend/holiday work counts as worked hours like any other day.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Overtime:ActivityCategories` | Dovolená→Vacation, Nemoc→Sick, Sick day→Sick, Lékař→Doctor, OČR→Ocr, Náhradní volno→CompTime | Logeto activity name (case-insensitive) → category |
| `Logeto:*` | see `feed-logeto-break-insertion` | Logeto client |

The úvazek is not configuration: it comes only from the Logeto Note (marker `integration`,
fixed — `Logeto:*:NoteMarker` does not apply here).

## Runtime facts
None.

## Known quirks
- **Open months show the whole month's requirement.** Required hours run to the last day of the
  month even mid-month, so the current month's delta is strongly negative until the month ends.
- **The úvazek has no history.** Every open month uses today's Note; a changed úvazek rewrites
  the requirement of all still-open months. Closed months keep their frozen `RequiredHours`.
- **Breaks are not subtracted.** Worked time is the sum of Work records; a break only matters
  if the Work record was actually cut around it (`feed-logeto-break-insertion`). A worker's own
  break placed inside a continuous Work record leaves the overlap counted as work.
- **Absences entered without hours count 0** unless `feed-logeto-absence-hours` filled them
  (it skips half days, days with two absences and people without úvazek in the Note).
- **"Ostatní" mixes credited and uncredited hours**: OČR (credited) and unmapped activities (not
  credited) both land in `OtherAbsenceHours`.
- **A GET writes to the DB** (open-statement cache); concurrent first views are handled by the
  unique index (`PersonId`, `Year`, `Month`) and a reload.
- **Any Logeto failure fails the whole page** for an open month (the error is returned instead
  of statements); closed months still load.
- `IContractHoursProvider`'s doc comment still says "configuration-backed"; the live
  implementation is `LogetoContractHoursProvider` (Note-based).
- Holidays are hard-coded; a new public holiday needs a code change.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/OvertimeCalculationService.cs` — categories, hours, delta
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/WorkingDaysCalculator.cs`, `CzechHolidays.cs` — working days
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs` — úvazek from the Note
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/GetMonthlyStatements/GetMonthlyStatementsHandler.cs` — caching, balances, closed vs open
- `docs/superpowers/specs/2026-08-10-overtime-ledger-design.md` — design
