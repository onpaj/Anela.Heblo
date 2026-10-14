---
process: flow-overtime-month-close
kind: workflow
module: attendance
summary: Monthly overtime routine on /overtime — review each worker, record adjustments, close the month to freeze the numbers and carry the balance, then publish the "Evidence přesčasů" Excel to SharePoint.
owns:
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CloseMonth/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/SetStatementReviewed/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CreateAdjustment/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/DeleteAdjustment/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/ExportOvertimeReport/**
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/OvertimeExcelBuilder.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/*OvertimeReportPublisher.cs
  - backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs
  - backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs
  - backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/**
  - backend/src/Anela.Heblo.Persistence/Attendance/**
verified_at: "5e993f9e2"
related:
  - calc-overtime
---

# Overtime month close and Excel publish

## Purpose
Turns the live overtime numbers into the agreed, final record for a month. The office
reconciles each worker against Logeto and their notes, records payouts, purchase deductions and
corrections, then closes the month: numbers freeze, the balance carries forward, and the shared
Excel "Evidence přesčasů" that workers read is regenerated on SharePoint. Closed is final — there
is no reopen.

## Trigger
User-driven on `/overtime` (permission `Attendance_Overtime`; every step except viewing and
downloading needs Write):

| Step | UI | API |
|---|---|---|
| Review a person | "reviewed" tick per row | `POST /api/overtime/statements/{y}/{m}/reviewed` |
| Add / delete adjustment | row detail (`StatementAdjustmentsPanel`) | `POST /api/overtime/adjustments`, `DELETE /api/overtime/adjustments/{id}` |
| Close month | "Uzavřít měsíc" → `CloseOvertimeMonthDialog` | `POST /api/overtime/close/{y}/{m}?force=` |
| Download Excel | "Stáhnout Excel" (Read is enough) | `GET /api/overtime/export` |
| Re-upload Excel | "Nahrát na SharePoint" | `POST /api/overtime/export/publish` |

Statement states: (no row) → **Open** (row created on first view, see `calc-overtime`) →
**Closed**.

## Data flow
1. **Review**: sets `IsReviewed` on the person's open statement in
   `public."OvertimeMonthlyStatements"`. Refused if the month has a closed statement or the person
   has no statement row yet (`OvertimeEmployeeNotFound`).
2. **Adjustment**: inserts/deletes a row in `public."OvertimeAdjustments"` (signed hours,
   type, note ≤ 500 chars, `CreatedBy` = current user, `CreatedAtUtc`). Refused for an untracked
   person or a month with a closed statement (`OvertimeAdjustmentMonthClosed`).
3. **Close month** (`CloseMonthHandler`), checks in order:
   - month already has a closed statement → `OvertimeMonthAlreadyClosed`;
   - an older month has an Open statement → `OvertimePreviousMonthOpen`;
   - every month from the earliest active `BaselineDate` up to the previous month must have a
     closed statement, else `OvertimePreviousMonthOpen` naming that month;
   - recompute the month live from Logeto for active employees (`calc-overtime`); anyone without
     úvazek → `OvertimeContractHoursMissing` with names;
   - unless `force`, everyone must be reviewed → `OvertimeMonthNotReviewed` with names (the
     dialog lets the user confirm closing anyway).
4. **Freeze** per active person: copy the fresh hours into the statement (creating it if
   missing), then `BalanceAfter = previous balance + DeltaHours + Σ month's adjustments`, where
   previous balance = latest closed `BalanceAfter`, else `BaselineHours`. `Status = Closed`,
   `ClosedAtUtc`, `ClosedBy`. Open statements of people deactivated meanwhile are closed too,
   with their last cached delta. One `SaveChanges`.
5. **Publish** (only if `Overtime:ExportDriveId` is set and real Microsoft Identity auth is on):
   build the workbook from **all** closed statements and all adjustments and `PUT` it to
   `https://graph.microsoft.com/v1.0/drives/{ExportDriveId}/root:/{ExportFolderPath}/{ExportFileName}:/content?@microsoft.graph.conflictBehavior=replace`
   with an app-only token (scope `https://graph.microsoft.com/.default`). Failure → the close
   still succeeds, response `PublishFailed = true`; not configured → `PublishSkipped = true`.
6. **Download / re-upload** builds the same workbook on demand; re-upload uses the same publisher
   and returns `OvertimeExportPublishFailed` on error.

## Logic & formulas
- Workbook "Evidence přesčasů" (ClosedXML): one sheet per closed month named `YYYY-MM`, newest
  first; an "Info" sheet if nothing is closed. Columns: Zaměstnanec, Převod z minula
  (= `BalanceAfter − DeltaHours − adjustments`), Úvazek (h) (= required hours of the month),
  Odpracováno, Dovolená, Nemoc, Lékař, Náhradní volno, Ostatní, Rozdíl, Korekce (h),
  Korekce – detail (`Type: hours h – note; …`), Nový zůstatek.
- Adjustment hours −1000…1000, may be 0 (e.g. SportBenefit as a note).
- The baseline of an employee cannot change once they have a closed month
  (`UpsertOvertimeEmployeeHandler`).
- Corrections to a closed month go in as a `Correction` adjustment in a later open month.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Overtime:ExportDriveId` | "" | SharePoint drive id; empty = publishing disabled |
| `Overtime:ExportFolderPath` | "" | Folder inside the drive; empty = root |
| `Overtime:ExportFileName` | Evidence-prescasu.xlsx | File name (overwritten each time) |
| `UseMockAuth`, `BypassJwtValidation` | false | Either true → no-op publisher (publishing skipped) |

## Runtime facts
None. Whether `Overtime:ExportDriveId` is set in production is not recorded.

## Known quirks
- **No reopen.** A wrongly closed month can only be offset by a Correction in a later month (or
  fixed in the DB by hand).
- **Logeto edits after close are not picked up**: closed statements never recompute, so a later
  break insertion or absence fill over a closed month changes Logeto but not the ledger.
- **The first close must be the earliest baseline month**; every month since then has to be
  closed in order, even months nobody viewed.
- **The "previous month open" message can name the wrong month**: when the walk finds a month
  with no closed statement, it reports that month's year/month, and the Czech text reads "Nelze
  uzavřít {that month} — existuje neuzavřený starší měsíc".
- **Adjustments in a month that never gets closed are ignored**, e.g. a month before the
  person's baseline (nothing stops creating one there).
- **Deactivated employees are closed with stale numbers**: their open statement keeps the last
  cached computation, not a fresh one.
- **The close is one DB save, the publish is separate**: a failed upload leaves the month
  closed; use "Nahrát na SharePoint" to retry.
- The whole Excel is rebuilt and overwritten each time, so manual edits to the SharePoint file
  are lost on the next close or re-upload.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CloseMonth/CloseMonthHandler.cs` — checks, freeze, balance, publish
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/OvertimeExcelBuilder.cs` — workbook layout
- `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/GraphOvertimeReportPublisher.cs` — SharePoint upload
- `backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs` — endpoints and permissions
- `backend/src/Anela.Heblo.Persistence/Attendance/OvertimeStatementRepository.cs` — open/closed queries
- `frontend/src/pages/OvertimePage.tsx`, `frontend/src/components/dialogs/CloseOvertimeMonthDialog.tsx` — UI
