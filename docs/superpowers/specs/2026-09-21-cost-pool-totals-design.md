# Cost pool totals (M1 / M2 / M3) — design

**Date:** 2026-09-21
**Status:** Approved, pending implementation plan

## Problem

The margin engine loads spend from the Flexi ledger in two providers making
three department-filtered pulls, all inside
`Anela.Heblo.Application/Features/Catalog/CostProviders`:

| Level | Departments | Accounts | File |
|---|---|---|---|
| M1_A | `VYROBA` | `51`, `52` | `FlatManufactureCostProvider.cs:111` |
| M2 | `SKLAD`, `MARKETING` | `51`, `52` | `SalesCostProvider.cs:113-122` |

Two problems follow from this.

**The remaining spend is never loaded.** Everything outside those three
departments — centrála, režie, and anything unassigned — is not read by any
code path. There is no M3.

**Even the M2 total is unobtainable.** `SalesCostProvider` computes
`warehouseCosts.Sum() + marketingCosts.Sum()` into a local variable, divides it
by sold pieces on the next line, and discards it. The pool total is never
cached, persisted, or exposed. Nothing can answer "what was M2 spend in July".

`FinancialAnalysisService` is the only other ledger consumer. It reads accounts
`5`+`6` across all departments for the Financial analysis page and produces
income/expenses per month. It never buckets by cost centre, so it cannot answer
the question either.

## Goal

An internal backend service that computes monthly spend totals per cost pool,
keeps them, and serves them to other backend features on demand.

## Non-goals

- **No per-product allocation.** M3 does not become a margin level. `MarginData`
  is untouched; no `M3` property, no DTO change, no generated-client change, no
  frontend change.
- **No HTTP endpoint.** The consumer is another backend feature. No MediatR
  request/response pair, so the `*Response : BaseResponse` contract test does
  not apply.
- **No database migration.** Nothing is persisted; migrations here are manual
  and this data is cheaply re-derivable.
- **No change to existing margin numbers.** M1_A and M2 margins must come out
  bit-identical after this change.

## Pool definition

Bucketed by department, with the account set depending on the pool:

```
VYROBA                       -> M1    accounts 51, 52
SKLAD, MARKETING             -> M2    accounts 50, 51, 52
BUVOL                        -> none  (separate activity, not Anela overhead)
everything else              -> M3    accounts 51, 52 (catch-all, incl. null/empty)
```

**Why M2 is wider.** 50x in the warehouse and marketing is shipping packaging
(SERVISBAL, printed tape) and marketing print — real fulfilment and marketing
spend, ~4.3 % of the M2 pool. The same prefix in centrala is cost of goods sold,
an order of magnitude larger than every pool combined, so it must not fall into
the M3 catch-all. Hence the prefix set is per-pool
(`CostPoolDefinition.AccountPrefixesFor`) rather than global.

**Why BUVOL is excluded.** It shares the ledger but is a separate activity, so
unlike an unmapped cost centre it must not be absorbed by M3.

M3 is defined as the **complement**, not an explicit department list. A cost
centre added in Flexi later lands in M3 automatically rather than vanishing from
the totals. The invariant this buys us is testable:

```
M1 + M2 + M3 == total spend on accounts 51+52 for the period
```

The accepted downside is that a miscoded entry silently becomes overhead. We
mitigate by logging, at each refresh, the distinct department codes that fell
into M3 — a new code appearing in that log is the signal that something was
miscoded or that a genuine new cost centre needs promoting to its own pool.

M1 is included even though only M2 and M3 were asked for: it comes free from the
same grouped query, and without it the balance invariant above cannot be
asserted.

Note the invariant is over what the pools are *entitled* to, not over the raw
ledger pull: the single pull asks for the union of every pool's prefixes, so
entries must be bucketed through `CostPoolDefinition.Resolve(department, account)`,
which returns null for an excluded department and for an account that pool does
not count.

## Architecture

### Data source

`ILedgerService`, the same path `SalesCostProvider` already uses. This is a
deliberate choice over querying the locally-synced `flexi_raw.ledger_entry`
table.

The ledger *is* already persisted locally — `LedgerSyncService` backfills from
2020-01-01 and re-syncs nightly at 03:00 in all three environments, and a SQL
`GROUP BY` would be faster and reach further back. It was rejected for now on
two grounds:

1. **Divergence risk.** The new service's M2 figure must equal the one the
   margin engine uses. Reading a different source makes that a coincidence
   rather than a guarantee.
2. **Unverified shape.** `LedgerSyncService.cs:145,149` stores
   `AccountDebit = dto.DebitAccountShowAs` and `CostCenter = dto.DepartmentRef`
   — display and reference strings — whereas the live path normalises to
   `Department.Code` (`LedgerMappingProfile.cs:22`). Prefix-matching `51`/`52`
   and matching `SKLAD` against those columns needs normalisation that has not
   been validated.

Revisit if deep history (pre-window) is ever needed.

### Query shape

One unfiltered call, which is intended to replace the three
department-filtered ones:

```csharp
_ledgerService.GetLedgerItems(from, to, debitAccountPrefix: ["51", "52"])
```

then group by `(month, department)` in memory and fold departments into pools.

**Traffic note.** As shipped this is a *fourth* pull, not a replacement: the
cost providers keep their three filtered pulls until the dedup follow-up lands,
and `LedgerService`'s 15-minute memory cache keys on the filter set, so the new
unfiltered result is held alongside them rather than instead of them. The
reduction to a single pull is contingent on that follow-up.

Summation matches `LedgerService.GetCosts` exactly — sum `item.Amount` over the
returned items, trusting the server-side debit-prefix filter rather than
re-checking client-side. `FinancialAnalysisService` does re-check
(`StartsWith("5")`), but matching `GetCosts` is what keeps M2 identical to the
margin engine's M2, and that consistency is the priority here.

### Components

```
Anela.Heblo.Domain/Accounting/CostPools/
  CostPool.cs             enum { M1, M2, M3 }
  MonthlyCostPool.cs      record { DateTime Month, CostPool Pool, decimal Amount }
  ICostPoolService.cs     contract
  ICostPoolCache.cs       storage contract

Anela.Heblo.Application/Shared/CostPools/
  CostPoolService.cs      bucketing logic
  CostPoolCache.cs        IMemoryCache wrapper
  CostPoolDefinition.cs   department -> pool map, constants
  SharedCostPoolsModule.cs  AddSharedCostPoolsModule()
```

`MonthlyCostPool` may be a record: it is an internal domain type that never
crosses the OpenAPI boundary, so the "DTOs are classes, never records" rule in
CLAUDE.md does not bind it. `Month` is a `DateTime` set to the first of the
month, matching the existing `MonthlyCost` value object.

The contract sits in `Domain/Accounting/` beside `ILedgerService`, so consuming
features depend on a Domain type and no module-boundary rule is engaged
(`ModuleBoundariesTests` defines rules pairwise between feature modules; a
Domain contract is not a violation).

The implementation sits in `Application/Shared/` rather than under a feature,
matching `Shared/Rag` and `Shared/Users` — it is cross-cutting, and the consumer
is a feature not yet written. Registered from `ApplicationModule` alongside
`AddSharedRagModule`.

### Contract

```csharp
public interface ICostPoolService
{
    Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    Task RefreshAsync(CancellationToken ct = default);
}
```

`GetMonthlyPoolsAsync` serves from cache when the cached window covers
`[from, to]`, and otherwise computes live through `ILedgerService` (which keeps
its own 15-minute memory cache). It never returns a silently-empty result for an
unhydrated cache — that is the behaviour of the existing cost providers and it
is wrong for a service another feature depends on.

Every month in the requested range appears in the result for every pool, with
`Amount = 0` where there was no spend. Callers never have to distinguish "no
data" from "no spend".

### Storage

`ICostPoolCache` over `IMemoryCache`, mirroring `SalesCostCache` — a pure
storage layer with the logic in the service. Hydrated by a background task:

```csharp
services.RegisterRefreshTask<ICostPoolService>(
    "RefreshCache",
    (s, ct) => s.RefreshAsync(ct));
```

The refresh window is `DataSourceOptions.ManufactureCostHistoryDays`, the same
window the other cost providers use, rounded out to whole months.

A persisted `cost_pool_monthly` table was considered and rejected: it stores a
copy of something re-derivable in one query, costs a manual migration, and only
pays off if figures must be *frozen* against later re-coding in Flexi. That is a
reporting requirement, and this is not a report. Revisit if that changes.

### Concurrency

`RefreshAsync` guards with a `SemaphoreSlim(1,1)` `WaitAsync(0)` and skips when a
refresh is already running, as `SalesCostProvider.cs:75-79` does.

### Error handling

Flexi failures propagate. `ILedgerService` already distinguishes an internal
HttpClient timeout from a caller abort and logs accordingly; the service adds no
catch-and-swallow. `RefreshAsync` logs the failure and rethrows so the background
refresh scheduler records it, matching `SalesCostProvider.cs:92-96`. Callers of
`GetMonthlyPoolsAsync` see the exception rather than a zeroed result — a wrong
zero in a downstream financial calculation is worse than a failure.

## Testing

Unit tests against a mocked `ILedgerService`:

- `VYROBA` entries land in M1; `SKLAD` and `MARKETING` in M2
- an unrecognised department lands in M3
- a null or empty department lands in M3
- **balance invariant**: M1 + M2 + M3 equals the sum of all ledger amounts
- **parity**: for a given period, the M2 total equals
  `warehouseCosts.Sum() + marketingCosts.Sum()` as `SalesCostProvider` computes
  it — this is the test that protects the "same number by construction" claim
- months with no entries return rows with `Amount = 0`, for every pool
- cache covering the range is served without touching `ILedgerService`
- a range not covered by the cache falls through to a live computation
- an unhydrated cache does not return empty
- a concurrent `RefreshAsync` is skipped, not queued
- a ledger failure propagates out of both methods

Backend gate: `dotnet build` + `dotnet format`, plus the full unit suite. No
frontend build needed — nothing on that side changes.

## Follow-up (out of scope)

`SalesCostProvider` and `FlatManufactureCostProvider` could consume
`ICostPoolService` and drop their own ledger pulls; they also duplicate
`GetDateRange` and `GenerateMonthRange` between them. Deliberately not done here
— it touches working margin code for no functional gain. Worth its own issue.
