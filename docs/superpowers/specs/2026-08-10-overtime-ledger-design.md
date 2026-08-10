# Overtime Ledger (Evidence přesčasů) — Design

**Date:** 2026-08-10
**Status:** Approved design, pending implementation plan
**Source:** Meeting transcript 2026-08-05 "Evidence práce a příprava mezd" (Andy + Ondra), brainstorming session 2026-08-10

## Context

Employees clock work time in Logeto (výkaz práce). Overtime is the company's informal
"currency": it accumulates month to month, is exchanged for comp time (náhradní volno),
paid out via bonuses, and reduced by employee purchases. Today the accumulated balance
lives in a hand-maintained Excel pinned in Teams. Andy rebuilds it every month: she
reconciles Logeto against employee notes, computes required vs. worked hours per person,
carries the balance forward, and applies manual deductions. This is error-prone and slow.

Two related pieces already exist or are in flight:

- **Break insertion job (shipped):** nightly job inserts the 30-min break into Logeto,
  removing the 6.9 h (with break) vs. 6.4 h (contract) duality.
- **Absence hours auto-fill (separate user story, in flight):** hour-less absence entries
  (vacation, sick, doctor, oČR) in Logeto get their hours filled automatically.
  **This design assumes absence entries carry hours.**
- **Logeto úvazek API investigation (separate session, in flight):** per-person daily
  contract hours (úvazek) will be read from Logeto. This design depends on that interface
  existing; it is abstracted behind `IContractHoursProvider`.

## Goals

1. Systemize month-to-month overtime accumulation per employee, replacing the internal Excel.
2. Compute each month's overtime delta automatically from Logeto data.
3. Give Andy an admin UI for manual adjustments (payouts, purchase deductions, corrections)
   with a full audit trail.
4. Explicit month-close workflow that freezes agreed numbers into the running balance.
5. Auto-generate the shared Excel (SharePoint) so nothing is hand-maintained.

## Non-goals

- Employee accounts in Heblo — employees keep using Logeto; they read the generated Excel.
- Live current-month standing sheet for employees (possible later phase).
- The accountant's payroll Excel (stays as-is, accountant-owned).
- Legal research on overtime compliance (separate task, not code).
- Absence hour auto-fill (separate user story).

## Approach

**Frozen monthly statements.** While a month is open its numbers are recomputed live from
Logeto; closing the month freezes the numbers into a persisted statement, commits the delta
to the accumulated balance, and regenerates the Excel. Closed months never change — even if
Logeto history is edited later. Corrections to closed months are made as adjustment entries
in a later open month. This mirrors Andy's actual workflow (review → employee confirmation →
commit) and produces an auditable record immune to retroactive edits.

## Data model

New vertical slice in the existing Attendance feature: `Features/Attendance/Overtime/`.
Entities persisted in PostgreSQL (manual migration, per project practice). All DTOs are
classes (never records); responses inherit `BaseResponse`.

### `OvertimeEmployee`

Which Logeto people are tracked and their starting point.

| Field | Notes |
|---|---|
| `Id` | PK |
| `PersonId` | Logeto person GUID, unique |
| `DisplayName` | shown in UI and Excel |
| `BaselineHours` | signed decimal; accumulated overtime seeded from today's Excel |
| `BaselineDate` | cutoff date; Logeto data before this date is never computed |
| `IsActive` | inactive employees keep history but are excluded from open months |

### `OvertimeMonthlyStatement`

One row per person per month, created when the month first comes under review.

| Field | Notes |
|---|---|
| `PersonId`, `Year`, `Month` | unique together |
| `Status` | `Open` / `Closed` |
| `RequiredHours` | working days × daily úvazek |
| `WorkedHours` | Σ work-type entries (breaks excluded) |
| `VacationHours`, `SickHours`, `DoctorHours`, `CompTimeHours`, `OtherAbsenceHours` | absence buckets |
| `DeltaHours` | worked + credited absences − required |
| `BalanceAfter` | previous balance + delta + month's adjustments; written on close |
| `IsReviewed` | Andy's per-person "done" checkmark during reconciliation |
| `ClosedAtUtc`, `ClosedBy` | audit |

While `Open`, hour fields are a refreshed cache of the live computation — Logeto is the
truth. On close they freeze and become the audit record.

### `OvertimeAdjustment`

Andy's manual moves, always attached to an open month.

| Field | Notes |
|---|---|
| `PersonId`, `Year`, `Month` | must reference an open month |
| `Type` | `Payout`, `PurchaseDeduction`, `Correction`, `SportBenefit`, `Other` |
| `Hours` | signed decimal; `SportBenefit` may be 0 (tracked note, not hours) |
| `Note` | free text (e.g. purchase detail) |
| `CreatedAtUtc`, `CreatedBy` | audit |

Adjustments are locked once their month closes. Late corrections go into the next open month.

## Computation

Activity classification maps Logeto activity names to categories via configuration
(same pattern as `BreakActivityName` today):
`Work`, `Break`, `Vacation`, `Sick`, `Doctor`, `Ocr`, `CompTime`, `Other`.

Per person, per open month:

```
WorkedHours      = Σ hours of Work-category entries
AbsenceCredit    = Σ hours of Vacation + Sick + Doctor + Ocr entries
CompTimeHours    = Σ hours of CompTime entries  → credited as ZERO
RequiredHours    = workingDays(month, Czech public holidays) × dailyÚvazek(person)
DeltaHours       = WorkedHours + AbsenceCredit − RequiredHours
```

Each configured category carries an `IsCredited` flag: `Vacation`, `Sick`, `Doctor`, `Ocr`
are credited; `CompTime`, `Break` are not; unmapped activities fall into `Other`, which is
**not credited** and is surfaced in the month detail so misclassified activities are visible
instead of silently counted.

- **Comp time credits zero hours.** Taking náhradní volno simply produces a negative delta
  that consumes the accumulated balance — exactly the current Excel formula. CompTime
  entries are still summed and displayed for visibility.
- **Czech public holidays** via a small holiday calculator or established library
  (e.g. Nager.Date) — working days = weekdays minus public holidays.
- **Daily úvazek** comes from `IContractHoursProvider` (Logeto-backed; interface isolates
  the in-flight API investigation). Value is the contract hours without break (6.4-style).
- Hours are kept at 2 decimal places; no additional rounding policy.
- Days before the person's `BaselineDate` are never computed.
- Break-category entries are never counted anywhere (they exist only to shape presence time).

## Month lifecycle

1. **Open:** statements materialize on first view; numbers refresh from Logeto on demand.
   Andy reconciles person by person and ticks `IsReviewed`.
2. **Close month** (single explicit action):
   - verifies all active employees are reviewed (override requires confirmation),
   - freezes every statement's numbers,
   - computes `BalanceAfter = previousBalance + DeltaHours + Σ adjustments` per person,
   - locks the month's adjustments,
   - triggers Excel regeneration.
3. **Closed is final.** No reopen. Corrections happen via `Correction` adjustments in a
   later open month.

Previous balance = `BalanceAfter` of the person's latest closed statement, or
`BaselineHours` if none exists.

## Excel output

- Generated on the backend with **ClosedXML** (MIT license).
- Workbook mirrors today's internal Excel: **one sheet per closed month**, columns:
  person, carried-over balance, required hours, vacation / sick / doctor / comp-time hours,
  worked hours, delta, adjustments (payouts, purchases with notes), sport benefit,
  new accumulated balance.
- **Delivery:** uploaded to a configured SharePoint folder via Microsoft Graph
  (app-only credentials in Azure Key Vault, `kv-heblo-stg` / `kv-heblo-prod`), overwriting
  the shared file pinned in Teams. Also downloadable directly from Heblo as fallback.
- The Graph upload adapter is isolated (own small adapter/service) so phase 1 can ship
  download-only if credentials/permissions drag on.

## API

MediatR + MVC controller, per project conventions. Endpoints (names indicative):

- `GET /api/overtime/employees` — tracked employees with current balances
- `PUT /api/overtime/employees/{personId}` — baseline / active flag config
- `GET /api/overtime/statements/{year}/{month}` — month detail, computed live for open months
- `POST /api/overtime/statements/{year}/{month}/reviewed` — toggle per-person reviewed flag
- `POST /api/overtime/adjustments` — create adjustment (open month only)
- `DELETE /api/overtime/adjustments/{id}` — remove adjustment (open month only)
- `POST /api/overtime/close/{year}/{month}` — close month
- `POST /api/overtime/export` — regenerate + upload Excel; `GET /api/overtime/export` — download

Validation via FluentValidation (validators registered manually per module, per project
gotcha). New error codes require the ErrorHandlingTests module-range bucket entry and the
Czech i18n translation.

## Frontend

One permission-gated admin page **"Evidence přesčasů"**:

- Person list with accumulated balances.
- Month detail: per-person computed numbers (required, worked, absence buckets, delta,
  projected balance), reviewed checkboxes, adjustment entry (type, hours, note),
  Close month button, export/download buttons.
- Employee config (baseline seeding, active flag) as a small settings section on the page.
- API hooks use absolute URLs (`${apiClient.baseUrl}${relativeUrl}`); generated TS client.

## Error handling

- Logeto API unavailable → open-month view shows a clear error; close is blocked.
- Missing úvazek for a tracked person → validation error on view/close, names the person.
- Absence entry without hours after baseline date → warning surfaced in the month detail
  (guards against the auto-fill story not having run).
- Close with unreviewed people → confirmation gate.
- SharePoint upload failure → close still succeeds; export marked failed and retryable,
  download always available.

## Dependencies & risks

| Dependency | Risk | Mitigation |
|---|---|---|
| Logeto úvazek API (separate session) | shape unknown | `IContractHoursProvider` interface; worst case: manual per-person hours config as fallback implementation |
| Absence hours auto-fill story | not yet shipped | engine surfaces hour-less absence entries as warnings; close can proceed only when clean |
| Microsoft Graph app credentials | provisioning delay | download-only fallback ships first |
| Baseline seeding accuracy | Excel numbers wrong | baseline is editable until the first month is closed for that person |

## Testing

- **Unit:** working-days calculator (holidays), delta computation (absence credit, comp-time
  zero credit, baseline cutoff), balance chaining across months, close math with adjustments,
  adjustment locking, activity classification mapping.
- **Integration:** statement/adjustment repositories, close idempotency (double-close is a
  no-op error), export generation.
- **Contract gates:** `*Response` inherits `BaseResponse`; validators registered; error
  codes covered by ErrorHandlingTests + i18n.
- **E2E (staging, nightly):** page loads, month detail renders, adjustment CRUD.

## Phasing

1. **Phase 1 (this design):** entities + computation engine + admin page + close workflow +
   Excel generation with download; SharePoint upload if credentials are ready.
2. **Later candidates (explicitly out of scope now):** live current-month sheet for
   employees, purchases/benefits fully absorbed from the side Excel, per-person
   confidential exports, compliance-driven reporting.
