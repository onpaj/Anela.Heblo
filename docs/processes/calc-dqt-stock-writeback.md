---
process: calc-dqt-stock-writeback
kind: calculation
module: data-quality
summary: Every morning lists yesterday's Heblo-to-Shoptet stock movements (StockUpOperations) that failed or are stuck, plus yesterday's stock-takings that recorded an error, as drift results.
owns:
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockOperationQuery.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockTakingQuery.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationStateSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockTakingSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/StockWriteBackMismatch.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs
verified_at: "5e993f9e2"
related:
  - feed-stock-up
---

# Stock write-back check (Zpětný zápis skladu)

## Purpose
When Heblo moves stock (a transport box is received, a gift package is assembled or taken
apart) it pushes the change to Shoptet as a *stock-up operation* (see `feed-stock-up`), and
stock-takings (inventura) write absolute stock to Shoptet or Flexi. If such a write fails,
the e-shop shows wrong stock and can oversell. This check gives a daily list of yesterday's
writes that did not go through, so someone can retry or accept them on
`/stock-up-operations` or redo the stock-taking. Results appear on the **Kvalita dat** page
(`/automation/data-quality`, test *Zpětný zápis skladu*); there is no dashboard tile.

Despite the job description ("between Shoptet and ABRA Flexi"), the check **reads only
Heblo's own tables** — it does not query Shoptet or Flexi, it reports the state Heblo recorded.

## Trigger
- Hangfire recurring job `daily-stock-writeback-dqt` (`StockWriteBackDqtJob`), cron
  `0 7 * * *` (07:00 Europe/Prague), enabled by default. Covers **yesterday**
  (`DateFrom = DateTo = UTC date − 1 day`).
- Manual: **Spustit DQT** → *Zpětný zápis skladu* with any date range
  (`TestType = StockWriteBackReconciliation`).

## Data flow
1. Create a `DqtRun` (`TestType` 3, Running) in `public."DqtRuns"`.
2. Window = `DateFrom 00:00:00` to `DateTo 23:59:59.9999999`, treated as **UTC**.
3. Read `public."StockUpOperations"` with `CreatedAt` in the window
   (`DataQualityStockOperationQueryAdapter`).
4. Read `public."StockTakingRecords"` with `Date` in the window, all stock-taking types
   (`DataQualityStockTakingQueryAdapter`).
5. Classify (`StockWriteBackDqtComparer`) and store each finding in
   `public."DqtDriftResults"`; run Completed with `TotalChecked` = operations + stock-taking
   records read, `TotalMismatches` = findings.

## Logic & formulas
- **Stuck threshold**: 1 hour (code default). An operation is *stuck* when its state is
  Pending or Submitted and it was created at or before *now − 1 h*.
- **Mismatch codes** (`StockWriteBackMismatch`, bit flags):

  | Code | Label (UI) | Rule | Row content |
  |---|---|---|---|
  | 1 `OperationFailed` | Operace selhala | Stock-up operation in state Failed | key = product code, Heblo = amount (pieces, signed), `Details` = `Doc: … \| State: … \| Error: …` |
  | 2 `OperationStuck` | Operace zaseknutá | Pending/Submitted and older than 1 h | as above |
  | 4 `StockTakingErrored` | Chyba inventury | Stock-taking record with a non-null `Error` | key = product code, Heblo = new amount (2 dp), `Details` = `Stock-taking error: …` |

  Completed operations and error-free stock-takings are counted but produce no row. The
  Shoptet column is always empty for this test.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-stock-writeback-dqt` | `0 7 * * *`, enabled | Schedule |

The 1-hour stuck threshold is a constructor default, not configurable.

## Runtime facts
None.

## Known quirks
- **It reports state at 07:00, not at the time of failure.** An operation that failed
  yesterday but was retried successfully before 07:00 is not reported; one created yesterday
  that is still Pending at 07:00 is reported as stuck (every one of yesterday's operations is
  older than 1 h by then).
- **Only yesterday's rows are looked at.** A failure older than yesterday that is still
  unresolved is never reported again by the scheduled run; use a manual run with a wider range
  or `/stock-up-operations`.
- **A Failed operation that was "accepted"** (`AcceptFailure`) becomes Completed and drops out
  of this check, even though the Shoptet stock was never changed.
- **UTC day window**: operations created between 00:00 and 01:00/02:00 Prague time belong to
  the previous UTC day. `StockUpOperations.CreatedAt` is set from `DateTime.UtcNow`; whether
  `StockTakingRecords.Date` is UTC or local is not enforced by the code.
- The run detail labels the value column "Heblo"; for stock-takings it is the target amount,
  not a Heblo stock level.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs` — rules, threshold
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs` — schedule, yesterday window
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapter.cs` — `StockUpOperations` read
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapter.cs` — `StockTakingRecords` read
- `backend/src/Anela.Heblo.Persistence/Logistics/StockTaking/StockTakingRepository.cs` — date filter
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs` — operation states (see `feed-stock-up`)
